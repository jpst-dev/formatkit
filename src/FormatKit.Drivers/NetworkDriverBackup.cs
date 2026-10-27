using FormatKit.Core.Steps;

namespace FormatKit.Drivers;

public sealed record BackupResult(
    IReadOnlyList<string> Exported,
    IReadOnlyList<string> SkippedInbox,
    IReadOnlyList<string> Failed);

public sealed class NetworkDriverBackup(ServiceSet services)
{
    public async Task<BackupResult> ExportAsync(string outputDirectory, CancellationToken ct)
    {
        var snapshot = await services.Hardware.ReadAsync(ct);
        services.Files.CreateDirectory(outputDirectory);

        var exported = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var adapter in snapshot.NetworkAdapters)
        {
            if (adapter.InfName?.StartsWith("oem", StringComparison.OrdinalIgnoreCase) != true)
            {
                skipped.Add(adapter.Name);
                continue;
            }

            var folder = UniqueName(Sanitize(adapter.Name), usedNames);
            var target = Path.Combine(outputDirectory, folder);
            var result = await services.PnpUtil.ExportDriverAsync(adapter.InfName, target, ct);
            (result.Success ? exported : failed).Add(adapter.Name);
        }

        return new BackupResult(exported, skipped, failed);
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c)]).Trim();
        return cleaned.Length == 0 ? "adapter" : cleaned;
    }

    private static string UniqueName(string name, HashSet<string> used)
    {
        var candidate = name;
        for (var n = 2; !used.Add(candidate); n++)
        {
            candidate = $"{name} ({n})";
        }

        return candidate;
    }
}
