using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Media.Setup;

namespace FormatKit.Media.Tests.Setup;

public class MachineLocaleTests
{
    private const string Preload = @"HKCU\Keyboard Layout\Preload";
    private const string Substitutes = @"HKCU\Keyboard Layout\Substitutes";

    [Fact]
    public void SubstituteIsUsedWhenPresent()
    {
        var registry = new FakeRegistry();
        registry.Set(Preload, "1", RegistryKind.String, "00000416");
        registry.Set(Substitutes, "00000416", RegistryKind.String, "00010416");
        Assert.Equal("0416:00010416", MachineLocale.Current(registry).Keyboard);
    }

    [Fact]
    public void PreloadAloneIsUsedWithoutSubstitute()
    {
        var registry = new FakeRegistry();
        registry.Set(Preload, "1", RegistryKind.String, "00000416");
        Assert.Equal("0416:00000416", MachineLocale.Current(registry).Keyboard);
    }

    [Fact]
    public void MissingPreloadIsNull() => Assert.Null(MachineLocale.Current(new FakeRegistry()).Keyboard);

    [Fact]
    public void LanguageAndTimeZoneComeFromTheHost()
    {
        var machine = MachineLocale.Current(new FakeRegistry());
        Assert.Equal(System.Globalization.CultureInfo.InstalledUICulture.Name, machine.UiLanguage);
        Assert.Equal(TimeZoneInfo.Local.Id, machine.TimeZone);
    }
}
