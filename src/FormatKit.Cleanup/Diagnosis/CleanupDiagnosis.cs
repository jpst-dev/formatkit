using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Diagnosis;

/// <summary>One matched issue. <paramref name="Detail"/> is the device name, the expanded registry path, or the scheme GUID.</summary>
public sealed record Finding(KnownIssue Issue, string Detail);

/// <param name="CanUndo">False when another user applied the item's per-user settings, so only that user can undo it (R15).</param>
public sealed record AppliedItem(string ItemId, bool CanUndo);

/// <summary>A journaled, unreverted item whose state was looked at. <paramref name="Checked"/> is false when the check could not run (needs admin, target absent), so it is not known to be reverted.</summary>
public sealed record RevertedItem(string ItemId, bool Checked);

/// <param name="NotChecked">Rules whose evaluation threw; each is logged (R16).</param>
/// <param name="RevertedByWindows">Applied items whose target no longer matches (informational), plus those that could not be checked.</param>
public sealed record DiagnosisReport(
    IReadOnlyList<Finding> Findings, IReadOnlyList<AppliedItem> Applied, string? JournalError, int NotChecked,
    IReadOnlyList<RevertedItem>? RevertedByWindows = null)
{
    public IReadOnlyList<RevertedItem> RevertedByWindows { get; init; } = RevertedByWindows ?? [];
}

/// <summary>Read-only: evaluates the known-issue rules and lists what the journal can still undo. Never writes to the system.</summary>
/// <param name="log">Hears rules that could not be evaluated.</param>
/// <param name="catalog">When given, journaled items still in it are checked for state Windows reverted.</param>
public sealed class CleanupDiagnosis(KnownIssues issues, AppPaths paths, Serilog.ILogger? log = null, CleanupCatalog? catalog = null)
{
    public async Task<DiagnosisReport> RunAsync(ServiceSet services, CancellationToken ct)
    {
        var store = new JournalStore(services.Files, paths);
        var load = store.Load();
        // A scheme cleanup itself created (ultimate-plan) is not a foreign import worth reporting.
        var ownSchemes = PowerSchemeHandler.CreatedSchemes(load.Journal);
        var findings = new List<Finding>();
        var notChecked = 0;
        foreach (var issue in issues.Issues)
        {
            IReadOnlyList<string> details;
            try
            {
                details = await EvaluateAsync(issue.Rule, services, ownSchemes, ct);
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

        var currentSid = services.UserSession.CurrentUserSid;
        var applied = load.Journal is null
            ? []
            : load.Journal.Entries
                .Where(e => e.RevertedAt is null)
                .GroupBy(e => e.ItemId, StringComparer.Ordinal)
                .Select(g => new AppliedItem(g.Key, g.All(e => e.CanBeUndoneBy(currentSid))))
                .ToList();

        var reverted = catalog is null || load.Journal is null
            ? []
            : await FindRevertedAsync(load.Journal, catalog, currentSid, services, store, ct);

        return new DiagnosisReport(
            sorted,
            applied,
            load.Status == JournalLoadStatus.Corrupt ? load.Error : null,
            notChecked,
            reverted);
    }

    /// <summary>Unreverted journaled items still in the catalog whose actions no longer hold. Items another user applied are skipped: their per-user state is not ours to read.</summary>
    private async Task<IReadOnlyList<RevertedItem>> FindRevertedAsync(
        CleanupJournal journal, CleanupCatalog catalog, string? currentSid, ServiceSet services, JournalStore store, CancellationToken ct)
    {
        var handlers = new ActionHandlers(() => PowerSchemeHandler.CreatedSchemes(store.Load().Journal), () => PowerSchemeHandler.FormatKitSchemes(store.Load().Journal));
        var applier = new ItemApplier(handlers, store);
        var ctx = new StepContext(services, log ?? Serilog.Core.Logger.None);
        var result = new List<RevertedItem>();
        foreach (var group in journal.Entries.Where(e => e.RevertedAt is null).GroupBy(e => e.ItemId, StringComparer.Ordinal))
        {
            if (catalog.Find(group.Key) is not { } item || !group.All(e => e.CanBeUndoneBy(currentSid)))
                continue;
            try
            {
                if (!await applier.IsAppliedAsync(item, ctx, ct))
                    result.Add(new RevertedItem(item.Id, true));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Access denied or an absent target: not known to be reverted.
                log?.Warning(ex, "Could not check whether Windows reverted cleanup item {Item}", item.Id);
                result.Add(new RevertedItem(item.Id, false));
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<string>> EvaluateAsync(IssueRule rule, ServiceSet services, IReadOnlyDictionary<Guid, Guid?> ownSchemes, CancellationToken ct)
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
                return BuiltInPowerSchemes.IsBuiltIn(active) || ownSchemes.ContainsKey(active) ? [] : [active.ToString()];
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
