using System.Globalization;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Media.Setup;

/// <summary>Language, keyboard and time zone of the PC running FormatKit, used as setup defaults.</summary>
public sealed record MachineLocale(string UiLanguage, string? Keyboard, string TimeZone)
{
    private const string PreloadKey = @"HKCU\Keyboard Layout\Preload";
    private const string SubstitutesKey = @"HKCU\Keyboard Layout\Substitutes";

    public static MachineLocale Current(IRegistry registry) =>
        new(CultureInfo.InstalledUICulture.Name, ReadKeyboard(registry), TimeZoneInfo.Local.Id);

    /// <summary>The first preloaded layout (mapped through Substitutes) as <c>&lt;language id&gt;:&lt;KLID&gt;</c>, or null.</summary>
    private static string? ReadKeyboard(IRegistry registry)
    {
        var preload = registry.Read(PreloadKey, "1");
        if (preload is not { Kind: RegistryKind.String } || string.IsNullOrWhiteSpace(preload.Data)) return null;
        var klid = preload.Data.Trim();
        var substitute = registry.Read(SubstitutesKey, klid);
        if (substitute is { Kind: RegistryKind.String } && !string.IsNullOrWhiteSpace(substitute.Data))
            klid = substitute.Data.Trim();
        // The original (preload) KLID carries the language id in its last four hex digits.
        var languageId = preload.Data.Trim();
        return languageId.Length < 4 ? null : $"{languageId[^4..]}:{klid}";
    }
}
