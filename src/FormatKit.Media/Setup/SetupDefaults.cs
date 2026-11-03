using FormatKit.Core.SystemAccess;
using FormatKit.Media.Localization;

namespace FormatKit.Media.Setup;

public static class SetupDefaults
{
    /// <summary>Fills the unset <c>setup</c> choices from the current PC and the image; <c>Error</c> names the offending field.</summary>
    public static (ResolvedSetup? Setup, string? Error) Resolve(SetupOptions options, MachineLocale machine, WindowsImageInfo image)
    {
        string language;
        if (options.Language is not null)
        {
            var match = Find(image, options.Language);
            if (match is null)
                return (null, Strings.Format("Setup_LanguageNotInImage", "setup.language", options.Language, string.Join(", ", image.Languages)));
            language = match;
        }
        else
        {
            language = Find(image, machine.UiLanguage)
                ?? (image.DefaultLanguageIndex >= 0 && image.DefaultLanguageIndex < image.Languages.Count
                    ? image.Languages[image.DefaultLanguageIndex]
                    : image.Languages[0]);
        }

        return (new ResolvedSetup(
            options.Account,
            options.Edition,
            options.ComputerName,
            language,
            options.Keyboard ?? machine.Keyboard ?? language,
            options.TimeZone ?? machine.TimeZone), null);
    }

    private static string? Find(WindowsImageInfo image, string language) =>
        image.Languages.FirstOrDefault(l => string.Equals(l, language, StringComparison.OrdinalIgnoreCase));
}
