using System.Text.Json;
using FormatKit.Cleanup.Catalog;

namespace FormatKit.Cleanup;

public sealed record CleanupOptions(
    bool Enabled,
    CleanupLevel? Level,
    IReadOnlyList<string> Include,
    IReadOnlyList<string> Exclude,
    bool ConfirmAggressive)
{
    internal static readonly IReadOnlyDictionary<string, CleanupLevel> LevelNames =
        new Dictionary<string, CleanupLevel>(StringComparer.Ordinal)
        {
            ["safe"] = CleanupLevel.Safe,
            ["balanced"] = CleanupLevel.Balanced,
            ["aggressive"] = CleanupLevel.Aggressive,
        };

    /// <summary>Reads the section leniently; <see cref="CleanupModule.Validate"/> reports malformed input.</summary>
    internal static CleanupOptions From(JsonElement section)
    {
        var enabled = false;
        CleanupLevel? level = null;
        var include = new List<string>();
        var exclude = new List<string>();
        var confirm = false;

        if (section.ValueKind == JsonValueKind.Object)
        {
            if (section.TryGetProperty("enabled", out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False)
                enabled = e.GetBoolean();

            if (section.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.String
                && LevelNames.TryGetValue(l.GetString()!, out var parsed))
                level = parsed;

            ReadIds(section, "include", include);
            ReadIds(section, "exclude", exclude);

            if (section.TryGetProperty("confirmAggressive", out var c) && c.ValueKind is JsonValueKind.True or JsonValueKind.False)
                confirm = c.GetBoolean();
        }

        return new CleanupOptions(enabled, level, include, exclude, confirm);
    }

    private static void ReadIds(JsonElement section, string name, List<string> ids)
    {
        if (!section.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                ids.Add(item.GetString()!);
        }
    }
}
