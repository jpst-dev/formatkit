using System.Text.Json;

namespace FormatKit.Drivers;

public enum DriverSource { WindowsUpdate, Oem, Gpu }

public sealed record DriversOptions(bool Enabled, IReadOnlyList<DriverSource> Sources, string? NetworkDriverBackup)
{
    internal static readonly IReadOnlyDictionary<string, DriverSource> SourceNames =
        new Dictionary<string, DriverSource>(StringComparer.Ordinal)
        {
            ["windowsUpdate"] = DriverSource.WindowsUpdate,
            ["oem"] = DriverSource.Oem,
            ["gpu"] = DriverSource.Gpu,
        };

    /// <summary>Reads the section leniently; <see cref="DriversModule.Validate"/> reports malformed input.</summary>
    internal static DriversOptions From(JsonElement section)
    {
        var enabled = false;
        var sources = new List<DriverSource>();
        string? backup = null;

        if (section.ValueKind == JsonValueKind.Object)
        {
            if (section.TryGetProperty("enabled", out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False)
                enabled = e.GetBoolean();

            if (section.TryGetProperty("sources", out var s) && s.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in s.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && SourceNames.TryGetValue(item.GetString()!, out var src))
                        sources.Add(src);
                }
            }

            if (section.TryGetProperty("networkDriverBackup", out var b) && b.ValueKind == JsonValueKind.String)
                backup = b.GetString();
        }

        return new DriversOptions(enabled, sources, backup);
    }
}
