using FormatKit.Core.Profiles;
using static FormatKit.Dev.Tests.DevTestData;

namespace FormatKit.Dev.Tests;

public class ExternalInputsTests
{
    private static DevModule Module() => new(TimeProvider.System, Env());

    [Fact]
    public void Local_dotfiles_path_is_a_folder_resolved_against_the_base_directory()
    {
        var json = Json("""{ "enabled": true, "dotfiles": { "path": "dots", "files": [ { "from": "x", "to": "%USERPROFILE%\\x" } ] } }""");

        var inputs = Module().ExternalInputs(json, @"E:\usb");

        Assert.Equal([new ExternalInput("modules.dev.dotfiles.path", @"E:\usb\dots", ExternalInputKind.Folder)], inputs);
    }

    [Fact]
    public void Dotfiles_repo_has_no_inputs()
    {
        var json = Json("""{ "enabled": true, "dotfiles": { "repo": "https://github.com/me/dots", "files": [ { "from": "x", "to": "%USERPROFILE%\\x" } ] } }""");

        Assert.Empty(Module().ExternalInputs(json, @"E:\usb"));
    }

    [Fact]
    public void Section_without_dotfiles_has_no_inputs()
    {
        Assert.Empty(Module().ExternalInputs(Json("""{ "enabled": true }"""), @"E:\usb"));
    }
}
