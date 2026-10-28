using FormatKit.Cleanup.Journal;
using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Diagnosis;

/// <summary>One matched issue. <paramref name="Detail"/> is the device name, the expanded registry path, or the scheme GUID.</summary>
public sealed record Finding(KnownIssue Issue, string Detail);

public sealed record AppliedItem(string ItemId, bool CanUndo);

/// <param name="NotChecked">Rules whose evaluation threw; each is logged (R16).</param>
public sealed record DiagnosisReport(IReadOnlyList<Finding> Findings, IReadOnlyList<AppliedItem> Applied, string? JournalError, int NotChecked);

/// <summary>Read-only: evaluates the known-issue rules and lists what the journal can still undo. Never writes to the system.</summary>
/// <param name="log">Hears rules that could not be evaluated.</param>
public sealed class CleanupDiagnosis(KnownIssues issues, AppPaths paths, Serilog.ILogger? log = null)
{
    public async Task<DiagnosisReport> RunAsync(ServiceSet services, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var notChecked = 0;
        foreach (var issue in issues.Issues)
        {
            IReadOnlyList<string> details;
            try
            {
                details = await EvaluateAsync(issue.Rule, services, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One unreadable value must not hide every other finding.
                log?.Warning(ex, "Could not evaluate known issue {Issue}", issue.Id);
                notChecked++;
                continue;
            }

            foreach (var detail in details)
                findings.Add(new Finding(issue, detail));
        }

        var sorted = findings
            .OrderBy(f => f.Issue.Severity)
            .ThenBy(f => f.Issue.Id, StringComparer.Ordinal)
            .ToList();

        var load = new JournalStore(services.Files, paths).Load();
        var applied = load.Journal is null
            ? []
            : load.Journal.Entries
                .Where(e => e.RevertedAt is null)
                .Select(e => e.ItemId)
                .Distinct(StringComparer.Ordinal)
                .Select(id => new AppliedItem(id, true))
                .ToList();

        return new DiagnosisReport(
            sorted,
            applied,
            load.Status == JournalLoadStatus.Corrupt ? load.Error : null,
            notChecked);
    }

    private static async Task<IReadOnlyList<string>> EvaluateAsync(IssueRule rule, ServiceSet services, CancellationToken ct)
    {
        switch (rule)
        {
            case DeviceDisabledRule device:
                var disabled = await services.Hardware.ReadDisabledDevicesAsync(ct);
                return disabled
                    .Where(d => d.HardwareIds.Any(id => HardwareIdMatches(id, device.HardwareId)))
                    .Select(d => d.DisplayName)
                    .ToList();
            case ServiceDisabledRule service:
                return services.ServiceControl.GetStartType(service.Service) == ServiceStartType.Disabled
                    ? [service.Service]
                    : [];
            case RegistryValueRule registry:
                return ExpandPath(registry.Path, services.Registry)
                    .Where(path => RegistryMatches(registry, services.Registry.Read(path, registry.Name)))
                    .ToList();
            case NonBuiltInPowerSchemeRule:
                var active = await services.Power.GetActiveAsync(ct);
                return BuiltInPowerSchemes.IsBuiltIn(active) ? [] : [active.ToString()];
            default:
                return [];
        }
    }

    /// <summary>The rule id itself or a child of it (R10), so <c>ACPI\PNP0103</c> never matches <c>ACPI\PNP01030</c>.</summary>
    private static bool HardwareIdMatches(string id, string rule) =>
        string.Equals(id, rule, StringComparison.OrdinalIgnoreCase)
        || id.StartsWith(rule + "\\", StringComparison.OrdinalIgnoreCase);

    private static bool RegistryMatches(RegistryValueRule rule, RegistryValue? value)
    {
        if (value is null)
            return false;
        return rule.EqualsValue is not null
            ? string.Equals(value.Data, rule.EqualsValue, StringComparison.Ordinal)
            : !string.Equals(value.Data, rule.NotEqualsValue, StringComparison.Ordinal);
    }

    private static IEnumerable<string> ExpandPath(string path, IRegistry registry)
    {
        var star = path.IndexOf('*');
        if (star < 0)
            return [path];
        var parent = path[..star].TrimEnd('\\');
        var rest = path[(star + 1)..];
        return registry.GetSubKeyNames(parent).Select(name => parent + "\\" + name + rest);
    }
}
