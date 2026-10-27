using System.Text.Json;

namespace FormatKit.Core.Profiles;

public static class BuiltInProfiles
{
    public const string DriversOnlyPath = "builtin:drivers";
    public const string DriversOnlyHash = "builtin-drivers-v1";

    public static Profile DriversOnly { get; } = CreateDriversOnly();

    private static Profile CreateDriversOnly()
    {
        using var doc = JsonDocument.Parse("""{"enabled":true,"sources":["windowsUpdate","oem","gpu"]}""");
        return new Profile(
            ProfileLoader.CurrentSchemaVersion,
            "Drivers only",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["drivers"] = doc.RootElement.Clone() });
    }
}
