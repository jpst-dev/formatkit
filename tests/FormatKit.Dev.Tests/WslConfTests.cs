namespace FormatKit.Dev.Tests;

public class WslConfTests
{
    [Fact]
    public void Wslconf_keeps_other_sections()
    {
        var result = WslConf.SetDefaultUser("[boot]\nsystemd=true\n", "joao");

        Assert.Equal("[boot]\nsystemd=true\n\n[user]\ndefault=joao\n", result);
    }

    [Fact]
    public void Empty_file_gets_only_the_user_section() =>
        Assert.Equal("[user]\ndefault=joao\n", WslConf.SetDefaultUser("", "joao"));

    [Fact]
    public void Existing_default_is_replaced_and_neighbours_kept()
    {
        var result = WslConf.SetDefaultUser("[user]\ndefault=root\n[boot]\nsystemd=true\n", "joao");

        Assert.Equal("[user]\ndefault=joao\n[boot]\nsystemd=true\n", result);
    }

    [Fact]
    public void User_section_without_default_gets_one()
    {
        var result = WslConf.SetDefaultUser("[user]\n[boot]\nsystemd=true\n", "joao");

        Assert.Equal("[user]\ndefault=joao\n[boot]\nsystemd=true\n", result);
    }

    [Fact]
    public void Already_correct_file_is_returned_unchanged()
    {
        const string text = "[boot]\nsystemd=true\n[user]\ndefault=joao\n";

        Assert.Equal(text, WslConf.SetDefaultUser(text, "joao"));
    }

    [Fact]
    public void Wslconfig_has_only_the_given_keys()
    {
        Assert.Equal("[wsl2]\nmemory=8GB\nprocessors=4\nswap=0\n", WslConf.WslConfig(new WslConfig("8GB", 4, "0")));
        Assert.Equal("[wsl2]\nprocessors=2\n", WslConf.WslConfig(new WslConfig(null, 2, null)));
    }
}
