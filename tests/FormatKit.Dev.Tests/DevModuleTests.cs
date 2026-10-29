using static FormatKit.Dev.Tests.DevTestData;

namespace FormatKit.Dev.Tests;

public class DevModuleTests
{
    private static DevModule Module() => new(TimeProvider.System, Env());

    [Fact]
    public void Name_is_dev() => Assert.Equal("dev", Module().Name);

    [Fact]
    public void Only_an_enabled_section_is_enabled()
    {
        Assert.True(Module().IsEnabled(Json("""{ "enabled": true }""")));
        Assert.False(Module().IsEnabled(Json("""{ "enabled": false }""")));
        Assert.False(Module().IsEnabled(Json("""{}""")));
    }

    [Fact]
    public void Validate_reports_errors_under_the_given_path()
    {
        var errors = Module().Validate(Json("""{ "enabled": true, "wsl": { "user": "root" } }"""), "modules.dev");

        Assert.Equal("modules.dev.wsl.user", Assert.Single(errors).Path);
    }

    [Fact]
    public void Validate_accepts_a_relative_dotfiles_path_with_a_base_directory()
    {
        var json = Json("""{ "enabled": true, "dotfiles": { "path": "d", "files": [ { "from": "x", "to": "%USERPROFILE%\\x" } ] } }""");

        Assert.Empty(Module().Validate(json, "modules.dev", @"D:\profiles"));
    }

    [Fact]
    public void Wsl_section_creates_the_wsl_step()
    {
        var steps = Module().CreateSteps(Json("""{ "enabled": true, "wsl": { "user": "joao" } }"""));

        Assert.Equal("dev.wsl", Assert.Single(steps).Id);
    }

    [Fact]
    public void Empty_or_disabled_section_creates_no_steps()
    {
        Assert.Empty(Module().CreateSteps(Json("""{ "enabled": true }""")));
        Assert.Empty(Module().CreateSteps(Json("""{ "enabled": false, "git": { "name": "x" } }"""), @"D:\profiles"));
    }
}
