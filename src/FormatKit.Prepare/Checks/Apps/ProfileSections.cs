using System.Text.Json;

namespace FormatKit.Prepare.Checks.Apps;

/// <summary>Reads the loaded profile the way the modules do, for checks that depend on a section or an external input.</summary>
internal static class ProfileSections
{
    /// <summary>True when the section exists and has <c>"enabled": true</c> (as each module's <c>IsEnabled</c> reads it).</summary>
    public static bool IsEnabled(PrepareContext context, string module) =>
        context.Profile?.Profile.Modules.TryGetValue(module, out var section) == true
        && section.ValueKind == JsonValueKind.Object
        && section.TryGetProperty("enabled", out var enabled)
        && enabled.ValueKind == JsonValueKind.True;

    public static FormatKit.Core.Profiles.ExternalInput? Input(PrepareContext context, string jsonPath) =>
        context.Inputs.FirstOrDefault(i => string.Equals(i.JsonPath, jsonPath, StringComparison.Ordinal));

    public static string SystemDriveLabel(PrepareContext context) => $"{char.ToUpperInvariant(context.Env.SystemDrive)}:";
}
