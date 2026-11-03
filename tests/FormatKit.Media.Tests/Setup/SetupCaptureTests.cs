using FormatKit.Core.Capture;
using FormatKit.Media.Setup;

namespace FormatKit.Media.Tests.Setup;

public class SetupCaptureTests
{
    private static readonly MachineLocale Locale = new("pt-BR", "0416:00010416", "E. South America Standard Time");

    [Fact]
    public void Reads_locale_and_name()
    {
        var section = SetupCapture.Run(Locale, "PC-JOAO");

        Assert.Equal("PC-JOAO", section.Value["computerName"]!.GetValue<string>());
        Assert.Equal("pt-BR", section.Value["language"]!.GetValue<string>());
        Assert.Equal("0416:00010416", section.Value["keyboard"]!.GetValue<string>());
        Assert.Equal("E. South America Standard Time", section.Value["timeZone"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("A-NAME-THAT-IS-TOO-LONG")]
    [InlineData("PC_JOAO")]
    public void Invalid_computer_name_is_dropped_with_a_note(string name)
    {
        var section = SetupCapture.Run(Locale, name);

        Assert.Null(section.Value["computerName"]);
        Assert.Equal("pt-BR", section.Value["language"]!.GetValue<string>());
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.NotCaptured && n.Path == "setup.computerName");
    }

    [Fact]
    public void Null_keyboard_is_left_out()
    {
        var section = SetupCapture.Run(Locale with { Keyboard = null }, "PC");

        Assert.Null(section.Value["keyboard"]);
    }

    [Fact]
    public void Account_and_edition_are_absent_and_flagged_for_review()
    {
        var section = SetupCapture.Run(Locale, "PC");

        Assert.Null(section.Value["account"]);
        Assert.Null(section.Value["edition"]);
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.Review && n.Path == "setup");
    }

    [Fact]
    public void Output_passes_the_parser()
    {
        var section = SetupCapture.Run(Locale, "PC-JOAO");

        var (options, errors) = SetupSectionParser.Parse($$"""{"setup":{{section.Value.ToJsonString()}}}""");

        Assert.Empty(errors);
        Assert.Equal("PC-JOAO", options!.ComputerName);
    }
}
