using System.Text.Json;

namespace FormatKit.Dev.Tests;

public class TerminalFragmentTests
{
    [Fact]
    public void Fragment_uses_family_name()
    {
        using var doc = JsonDocument.Parse(TerminalFragment.Render("CascadiaCode"));

        var profiles = doc.RootElement.GetProperty("profiles").EnumerateArray().ToArray();
        Assert.Equal(
            ["{61c54bbd-c2c6-5271-96e7-009a87ff44bf}", "{574e775e-4f2a-5b96-ac1e-a2962a402336}"],
            profiles.Select(p => p.GetProperty("updates").GetString()));
        Assert.All(profiles, p => Assert.Equal("CaskaydiaCove Nerd Font", p.GetProperty("font").GetProperty("face").GetString()));
    }

    [Theory]
    [InlineData("Meslo", "MesloLGM Nerd Font")]
    [InlineData("CascadiaCode", "CaskaydiaCove Nerd Font")]
    [InlineData("FiraCode", "FiraCode Nerd Font")]
    [InlineData("JetBrainsMono", "JetBrainsMono Nerd Font")]
    [InlineData("Hack", "Hack Nerd Font")]
    public void Every_font_option_has_its_family(string font, string family) =>
        Assert.Equal(family, TerminalFragment.FontFamilies[font]);
}
