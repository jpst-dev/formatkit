using FormatKit.Core.SystemAccess;
using FormatKit.Media.Setup;

namespace FormatKit.Media.Tests.Setup;

public class SetupDefaultsTests
{
    private static WindowsImageInfo Image(int defaultIndex, params string[] languages) =>
        new(1, "Windows 11 Pro", 9, "10.0.26100.1", languages, defaultIndex);

    private static MachineLocale Machine(string ui = "pt-BR", string? keyboard = "0416:00010416") =>
        new(ui, keyboard, "E. South America Standard Time");

    [Fact]
    public void RequestedLanguageMissingFromImageIsAnErrorListingTheLanguages()
    {
        var (setup, error) = SetupDefaults.Resolve(new SetupOptions(Language: "pt-BR"), Machine(), Image(0, "en-US"));
        Assert.Null(setup);
        Assert.Contains("setup.language", error);
        Assert.Contains("en-US", error);
    }

    [Fact]
    public void MachineLanguageIsUsedWhenTheImageHasIt()
    {
        var (setup, _) = SetupDefaults.Resolve(new SetupOptions(), Machine("pt-BR"), Image(1, "en-US", "pt-BR"));
        Assert.Equal("pt-BR", setup!.Language);
    }

    [Fact]
    public void ImageDefaultIsUsedWhenTheMachineLanguageIsMissing()
    {
        var (setup, _) = SetupDefaults.Resolve(new SetupOptions(), Machine("en-US"), Image(0, "pt-BR"));
        Assert.Equal("pt-BR", setup!.Language);
    }

    [Fact]
    public void LanguageComparisonIgnoresCase()
    {
        var (setup, error) = SetupDefaults.Resolve(new SetupOptions(Language: "PT-br"), Machine(), Image(0, "pt-BR"));
        Assert.Null(error);
        Assert.Equal("pt-BR", setup!.Language);
    }

    [Fact]
    public void KeyboardAndTimeZoneFromSetupWinOverTheMachine()
    {
        var (setup, _) = SetupDefaults.Resolve(
            new SetupOptions(Keyboard: "0409:00000409", TimeZone: "UTC"), Machine(), Image(0, "pt-BR"));
        Assert.Equal("0409:00000409", setup!.Keyboard);
        Assert.Equal("UTC", setup.TimeZone);
    }

    [Fact]
    public void KeyboardAndTimeZoneDefaultToTheMachine()
    {
        var (setup, _) = SetupDefaults.Resolve(new SetupOptions(), Machine(), Image(0, "pt-BR"));
        Assert.Equal("0416:00010416", setup!.Keyboard);
        Assert.Equal("E. South America Standard Time", setup.TimeZone);
    }

    [Fact]
    public void MissingMachineKeyboardFallsBackToTheResolvedLanguage()
    {
        var (setup, _) = SetupDefaults.Resolve(new SetupOptions(), Machine(keyboard: null), Image(0, "pt-BR"));
        Assert.Equal("pt-BR", setup!.Keyboard);
    }

    [Fact]
    public void EditionComputerNameAndAccountPassThrough()
    {
        var (setup, _) = SetupDefaults.Resolve(
            new SetupOptions(SetupAccount.Microsoft, "Windows 11 Pro", "PC1"), Machine(), Image(0, "pt-BR"));
        Assert.Equal(SetupAccount.Microsoft, setup!.Account);
        Assert.Equal("Windows 11 Pro", setup.Edition);
        Assert.Equal("PC1", setup.ComputerName);
    }
}
