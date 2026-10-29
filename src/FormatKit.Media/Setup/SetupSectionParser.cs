using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FormatKit.Core.Profiles;
using FormatKit.Media.Localization;

namespace FormatKit.Media.Setup;

/// <summary>Reads and validates the top-level <c>setup</c> section of a profile.</summary>
public static partial class SetupSectionParser
{
    private static readonly string[] KnownProperties = ["account", "edition", "computerName", "language", "keyboard", "timeZone"];

    [GeneratedRegex(@"^[0-9A-Fa-f]{4}:[0-9A-Fa-f]{8}$")]
    private static partial Regex KeyboardLayoutId();

    [GeneratedRegex(@"^[A-Za-z0-9-]{1,15}$")]
    private static partial Regex ComputerNamePattern();

    public static (SetupOptions? Options, IReadOnlyList<ProfileError> Errors) Parse(string profileJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(profileJson, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException)
        {
            return (null, [Invalid("", "valid JSON")]);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (null, [Invalid("", "object")]);
            if (!doc.RootElement.TryGetProperty("setup", out var setup))
                return (new SetupOptions(), []);
            if (setup.ValueKind != JsonValueKind.Object)
                return (null, [Invalid("setup", "object")]);

            var errors = new List<ProfileError>();
            foreach (var prop in setup.EnumerateObject())
            {
                if (!KnownProperties.Contains(prop.Name, StringComparer.Ordinal))
                    errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, "setup." + prop.Name,
                        Strings.Format("Setup_UnknownProperty", prop.Name)));
            }

            var account = SetupAccount.Local;
            var accountText = Text(setup, "account", errors, "\"local\" or \"microsoft\"");
            if (accountText is not null)
            {
                if (accountText.Equals("local", StringComparison.OrdinalIgnoreCase)) account = SetupAccount.Local;
                else if (accountText.Equals("microsoft", StringComparison.OrdinalIgnoreCase)) account = SetupAccount.Microsoft;
                else errors.Add(Invalid("setup.account", "\"local\" or \"microsoft\""));
            }

            var edition = Text(setup, "edition", errors, "string");

            var computerName = Text(setup, "computerName", errors, "string");
            if (computerName is not null
                && (!ComputerNamePattern().IsMatch(computerName) || computerName.All(char.IsAsciiDigit)))
                errors.Add(Invalid("setup.computerName", "1-15 letters, digits or hyphens, not only digits"));

            var language = Text(setup, "language", errors, "string");
            if (language is not null && !IsCulture(language))
                errors.Add(Invalid("setup.language", "a language tag such as pt-BR"));

            var keyboard = Text(setup, "keyboard", errors, "string");
            if (keyboard is not null && !KeyboardLayoutId().IsMatch(keyboard) && !IsCulture(keyboard))
                errors.Add(Invalid("setup.keyboard", "a layout id such as 0416:00010416 or a language tag"));

            var timeZone = Text(setup, "timeZone", errors, "string");
            if (timeZone is not null && !IsTimeZone(timeZone))
                errors.Add(Invalid("setup.timeZone", "a time zone id"));

            return errors.Count > 0
                ? (null, errors)
                : (new SetupOptions(account, edition, computerName, language, keyboard, timeZone), []);
        }
    }

    private static string? Text(JsonElement setup, string name, List<ProfileError> errors, string expected)
    {
        if (!setup.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.String) return el.GetString();
        errors.Add(Invalid("setup." + name, expected));
        return null;
    }

    private static bool IsCulture(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        try
        {
            CultureInfo.GetCultureInfo(name, predefinedOnly: true);
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static bool IsTimeZone(string id)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return false;
        }
    }

    private static ProfileError Invalid(string path, string expected) =>
        new(ProfileErrorCode.InvalidValue, path, Strings.Format("Setup_InvalidValue", expected));
}
