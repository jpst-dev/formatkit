namespace FormatKit.Dev.Tests;

public class ZshRcTests
{
    internal const string Template = """
        export ZSH="$HOME/.oh-my-zsh"
        ZSH_THEME="robbyrussell"
        # ZSH_THEME_RANDOM_LIST=("robbyrussell" "agnoster")
        # Which plugins would you like to load?
        plugins=(git)
        source $ZSH/oh-my-zsh.sh

        """;

    [Fact]
    public void ZshRc_sets_theme_and_plugins_and_marker()
    {
        var text = ZshRc.Render(Template, "agnoster", ["git", "zsh-autosuggestions"]);

        Assert.StartsWith(ZshRc.Marker + "\n", text);
        Assert.Contains("\nZSH_THEME=\"agnoster\"\n", text);
        Assert.Contains("\nplugins=(git zsh-autosuggestions)\n", text);
        Assert.Contains("# ZSH_THEME_RANDOM_LIST=(\"robbyrussell\" \"agnoster\")", text);
        Assert.Contains("source $ZSH/oh-my-zsh.sh", text);
        Assert.DoesNotContain("robbyrussell\"\n", text.Replace("RANDOM_LIST", ""));
    }

    [Fact]
    public void Multi_line_plugin_list_is_replaced()
    {
        var text = ZshRc.Render("ZSH_THEME=\"x\"\nplugins=(\n  git\n  z\n)\nrest\n", "t", ["a"]);

        Assert.Equal(ZshRc.Marker + "\nZSH_THEME=\"t\"\nplugins=(a)\nrest\n", text);
    }

    [Fact]
    public void IsManaged_looks_at_the_first_line_only()
    {
        Assert.True(ZshRc.IsManaged(ZshRc.Marker + "\nexport A=1\n"));
        Assert.True(ZshRc.IsManaged("\uFEFF" + ZshRc.Marker + "\r\nexport A=1\r\n"));
        Assert.False(ZshRc.IsManaged("export A=1\n" + ZshRc.Marker + "\n"));
        Assert.False(ZshRc.IsManaged(ZshRc.Marker + " and more\n"));
        Assert.False(ZshRc.IsManaged(""));
    }
}
