using System.Text.Json;

namespace FormatKit.Dev;

/// <summary>The Windows Terminal fragment that sets the Nerd Font on the built-in PowerShell profiles (spec 6.5, 12).</summary>
internal static class TerminalFragment
{
    private const string WindowsPowerShellGuid = "{61c54bbd-c2c6-5271-96e7-009a87ff44bf}";
    private const string PowerShell7Guid = "{574e775e-4f2a-5b96-ac1e-a2962a402336}";

    /// <summary>The font option of the profile mapped to the family name the installed font registers.</summary>
    public static readonly IReadOnlyDictionary<string, string> FontFamilies = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Meslo"] = "MesloLGM Nerd Font",
        ["CascadiaCode"] = "CaskaydiaCove Nerd Font",
        ["FiraCode"] = "FiraCode Nerd Font",
        ["JetBrainsMono"] = "JetBrainsMono Nerd Font",
        ["Hack"] = "Hack Nerd Font",
    };

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, NewLine = "\n" };

    /// <summary>The fragment JSON for a font option (a key of <see cref="FontFamilies"/>).</summary>
    public static string Render(string font)
    {
        var face = FontFamilies[font];
        var fragment = new
        {
            profiles = new[] { WindowsPowerShellGuid, PowerShell7Guid }
                .Select(guid => new { updates = guid, font = new { face } })
                .ToArray(),
        };
        return JsonSerializer.Serialize(fragment, Indented) + "\n";
    }
}
