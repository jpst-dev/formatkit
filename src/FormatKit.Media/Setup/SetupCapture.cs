using System.Text.Json.Nodes;
using FormatKit.Core.Capture;
using FormatKit.Core.SystemAccess;
using FormatKit.Media.Localization;

namespace FormatKit.Media.Setup;

/// <summary>Captures the setup answers that are facts about this PC. The account type and the edition are choices, so they stay with the user.</summary>
public static class SetupCapture
{
    public static CaptureSection Run(IRegistry registry, string computerName) =>
        Run(MachineLocale.Current(registry), computerName);

    internal static CaptureSection Run(MachineLocale locale, string computerName)
    {
        var value = new JsonObject
        {
            ["computerName"] = computerName,
            ["language"] = locale.UiLanguage,
            ["timeZone"] = locale.TimeZone,
        };
        if (locale.Keyboard is not null)
            value["keyboard"] = locale.Keyboard;

        var notes = new List<CaptureNote> { new(CaptureNoteKind.Review, "setup", Strings.Get("Capture_AccountEdition")) };

        // The parser is the one definition of a valid answer; whatever it rejects is left out.
        var (_, errors) = SetupSectionParser.Parse(new JsonObject { ["setup"] = value.DeepClone() }.ToJsonString());
        foreach (var error in errors)
        {
            value.Remove(error.Path["setup.".Length..]);
            notes.Add(new CaptureNote(CaptureNoteKind.NotCaptured, error.Path, error.Message));
        }

        return new CaptureSection(value, notes, string.Join(", ", value.Select(p => p.Key)));
    }
}
