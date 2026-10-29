using FormatKit.Core.Profiles;
using static FormatKit.Dev.Tests.DevTestData;

namespace FormatKit.Dev.Tests;

public class DevOptionsTests
{
    private const string Path = "modules.dev";

    private static (DevOptions? Options, IReadOnlyList<ProfileError> Errors) Parse(string json, string? baseDirectory = null) =>
        DevOptions.Parse(Json(json), Path, baseDirectory, Env());

    private static DevOptions Valid(string json, string? baseDirectory = null)
    {
        var (options, errors) = Parse(json, baseDirectory);
        Assert.Empty(errors);
        return Assert.IsType<DevOptions>(options);
    }

    private static void AssertError(string json, string errorPath, ProfileErrorCode? code = null)
    {
        var (options, errors) = Parse(json);
        Assert.Null(options);
        var error = Assert.Single(errors, e => e.Path == errorPath);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        if (code is not null)
            Assert.Equal(code, error.Code);
    }

    private static string Wsl(string body) => $$"""{ "enabled": true, "wsl": { {{body}} } }""";
    private static string Git(string body) => $$"""{ "enabled": true, "git": { {{body}} } }""";
    private static string Ssh(string body) => $$"""{ "enabled": true, "ssh": { {{body}} } }""";
    private static string Dotfiles(string body) => $$"""{ "enabled": true, "dotfiles": { {{body}} } }""";
    private static string ShellWsl(string body) => $$"""{ "enabled": true, "wsl": { "user": "joao" }, "shell": { "wsl": { {{body}} } } }""";
    private static string ShellWindows(string body) => $$"""{ "enabled": true, "shell": { "windows": { {{body}} } } }""";
    private static string Drive(string body) => $$"""{ "enabled": true, "devDrive": { {{body}} } }""";

    private static string DriveWith(string path, string size = "50", string letter = "D") =>
        Drive($$""" "path": "{{J(path)}}", "sizeGB": {{size}}, "letter": "{{letter}}" """);

    private const string DriveOk = """ "path": "C:\\DevDrive\\dev.vhdx", "sizeGB": 100, "letter": "D" """;
    private const string File = """ "files": [ { "from": "git/.gitconfig.local", "to": "%USERPROFILE%\\.gitconfig.local" } ] """;

    [Fact]
    public void Full_profile_parses()
    {
        var options = Valid("""
            {
              "enabled": true,
              "wsl": { "distro": "Ubuntu-24.04", "user": "joao", "wslconfig": { "memory": "8GB", "processors": 4, "swap": "2GB" } },
              "git": { "name": "João Silva", "email": "joao@example.com", "defaultBranch": "main", "editor": "code --wait", "autocrlf": "input" },
              "ssh": { "keyType": "rsa", "comment": "joao@new-pc" },
              "dotfiles": { "repo": "https://github.com/joao/dotfiles.git", "cloneTo": "%USERPROFILE%\\code\\dotfiles",
                "files": [ { "from": "powershell/profile.ps1", "to": "%USERPROFILE%\\Documents\\PowerShell\\profile.ps1" } ] },
              "shell": { "wsl": { "zsh": true, "ohMyZsh": true, "theme": "agnoster", "plugins": ["git", "zsh-autosuggestions"] },
                         "windows": { "ohMyPosh": true, "theme": "jandedobbeleer", "font": "FiraCode" } },
              "devDrive": { "path": "C:\\DevDrive\\dev.vhdx", "sizeGB": 100, "letter": "e", "label": "My Dev", "caches": ["npm", "cargo"] }
            }
            """);

        Assert.Equal(new WslOptions("Ubuntu-24.04", "joao", new WslConfig("8GB", 4, "2GB")), options.Wsl);
        Assert.Equal(new GitOptions("João Silva", "joao@example.com", "main", "code --wait", "input"), options.Git);
        Assert.Equal(new SshOptions("rsa", "joao@new-pc"), options.Ssh);
        Assert.Equal(@"C:\Users\Joao\code\dotfiles", options.Dotfiles!.CloneTo);
        Assert.Equal(
            new DotfileEntry("powershell/profile.ps1", @"C:\Users\Joao\Documents\PowerShell\profile.ps1"),
            Assert.Single(options.Dotfiles.Files));
        var wsl = options.Shell!.Wsl!;
        Assert.True(wsl.Zsh);
        Assert.True(wsl.OhMyZsh);
        Assert.Equal("agnoster", wsl.Theme);
        Assert.Equal(["git", "zsh-autosuggestions"], wsl.Plugins);
        Assert.Equal(new WindowsShellOptions(true, "jandedobbeleer", "FiraCode"), options.Shell.Windows);
        var drive = options.DevDrive!;
        Assert.Equal((@"C:\DevDrive\dev.vhdx", 100, 'E', "My Dev"), (drive.Path, drive.SizeGB, drive.Letter, drive.Label));
        Assert.Equal(["npm", "cargo"], drive.Caches);
    }

    [Fact]
    public void Defaults_apply()
    {
        var options = Valid($$"""
            { "enabled": true, "wsl": { "user": "joao" }, "ssh": {}, "dotfiles": { "path": "D:\\d", {{File}} },
              "shell": { "wsl": {}, "windows": {} }, "devDrive": { {{DriveOk}} } }
            """);

        Assert.Equal("Ubuntu", options.Wsl!.Distro);
        Assert.Null(options.Wsl.Config);
        Assert.Equal("ed25519", options.Ssh!.KeyType);
        Assert.Equal(@"C:\Users\Joao\dotfiles", options.Dotfiles!.CloneTo);
        var wsl = options.Shell!.Wsl!;
        Assert.False(wsl.Zsh);
        Assert.False(wsl.OhMyZsh);
        Assert.Equal("robbyrussell", wsl.Theme);
        Assert.Equal(["git"], wsl.Plugins);
        Assert.Equal(new WindowsShellOptions(false, "paradox", "Meslo"), options.Shell.Windows);
        Assert.Equal("Dev", options.DevDrive!.Label);
        Assert.Empty(options.DevDrive.Caches);
    }

    [Fact]
    public void Wsl_unknown_distro() => AssertError(Wsl(""" "distro": "Fedora", "user": "joao" """), "modules.dev.wsl.distro");

    [Fact]
    public void Wsl_user_required() => AssertError(Wsl(""" "distro": "Debian" """), "modules.dev.wsl.user", ProfileErrorCode.MissingField);

    [Fact]
    public void Wsl_user_root_rejected() => AssertError(Wsl(""" "user": "root" """), "modules.dev.wsl.user");

    [Theory]
    [InlineData("Joao")]
    [InlineData("1joao")]
    [InlineData("joao silva")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Wsl_user_pattern(string user) => AssertError(Wsl($$""" "user": "{{user}}" """), "modules.dev.wsl.user");

    [Theory]
    [InlineData("memory", "8")]
    [InlineData("memory", "8gb")]
    [InlineData("swap", "2 GB")]
    public void Wslconfig_memory_pattern(string key, string value) =>
        AssertError(Wsl($$""" "user": "joao", "wslconfig": { "{{key}}": "{{value}}" } """), $"modules.dev.wsl.wslconfig.{key}");

    [Theory]
    [InlineData(9)]
    [InlineData(0)]
    public void Wslconfig_processors_above_count(int count) =>
        AssertError(Wsl($$""" "user": "joao", "wslconfig": { "processors": {{count}} } """), "modules.dev.wsl.wslconfig.processors");

    [Fact]
    public void Wslconfig_processors_equal_to_count_is_valid() =>
        Assert.Equal(8, Valid(Wsl(""" "user": "joao", "wslconfig": { "processors": 8 } """)).Wsl!.Config!.Processors);

    [Fact]
    public void Git_requires_a_key() => AssertError(Git(""), "modules.dev.git");

    [Fact]
    public void Git_email_needs_at() => AssertError(Git(""" "email": "joao.example.com" """), "modules.dev.git.email");

    [Fact]
    public void Git_autocrlf_values() => AssertError(Git(""" "autocrlf": "yes" """), "modules.dev.git.autocrlf");

    [Theory]
    [InlineData("name")]
    [InlineData("editor")]
    public void Git_control_characters_rejected(string key) => AssertError(Git($$""" "{{key}}": "a\nb" """), $"modules.dev.git.{key}");

    [Fact]
    public void Git_default_branch_pattern() => AssertError(Git(""" "defaultBranch": "ma in" """), "modules.dev.git.defaultBranch");

    [Theory]
    [InlineData("name")]
    [InlineData("editor")]
    [InlineData("defaultBranch")]
    public void Git_leading_dash_rejected(string key) => AssertError(Git($$""" "{{key}}": "-x" """), $"modules.dev.git.{key}");

    [Fact]
    public void Ssh_comment_default_is_made_to_fit_the_pattern()
    {
        var env = Env() with { UserName = "João Silva", ComputerName = "NEW PC" };

        var (options, errors) = DevOptions.Parse(Json(Ssh("")), Path, null, env);

        Assert.Empty(errors);
        Assert.Equal("jo-o-silva@new-pc", options!.Ssh!.Comment);
    }

    [Fact]
    public void Ssh_comment_default_is_truncated_to_100()
    {
        var env = Env() with { UserName = new string('a', 150) };

        var (options, _) = DevOptions.Parse(Json(Ssh("")), Path, null, env);

        Assert.Equal(100, options!.Ssh!.Comment.Length);
    }

    [Fact]
    public void Ssh_keytype_values() => AssertError(Ssh(""" "keyType": "dsa" """), "modules.dev.ssh.keyType");

    [Fact]
    public void Ssh_comment_default() => Assert.Equal("joao@new-pc", Valid(Ssh("")).Ssh!.Comment);

    [Fact]
    public void Ssh_comment_pattern() => AssertError(Ssh(""" "comment": "has space" """), "modules.dev.ssh.comment");

    [Fact]
    public void Dotfiles_repo_or_path_exactly_one()
    {
        AssertError(Dotfiles(File), "modules.dev.dotfiles");
        AssertError(Dotfiles($$""" "repo": "https://x.test/d.git", "path": "D:\\d", {{File}} """), "modules.dev.dotfiles");
    }

    [Theory]
    [InlineData("http://x.test/d.git")]
    [InlineData("ssh://git@x.test/d.git")]
    [InlineData("d.git")]
    public void Dotfiles_repo_scheme(string repo) =>
        AssertError(Dotfiles($$""" "repo": "{{repo}}", {{File}} """), "modules.dev.dotfiles.repo");

    [Fact]
    public void Dotfiles_scp_style_repo_is_valid() =>
        Assert.Equal("git@github.com:joao/d.git", Valid(Dotfiles($$""" "repo": "git@github.com:joao/d.git", {{File}} """)).Dotfiles!.Repo);

    [Theory]
    [InlineData("../secret")]
    [InlineData("a/../../b")]
    [InlineData("a\\..\\b")]
    [InlineData("C:\\abs")]
    public void Dotfiles_from_with_dotdot_rejected(string from) =>
        AssertError(
            Dotfiles($$""" "path": "D:\\d", "files": [ { "from": "{{J(from)}}", "to": "%USERPROFILE%\\x" } ] """),
            "modules.dev.dotfiles.files[0].from");

    [Theory]
    [InlineData("C:\\Windows\\x")]
    [InlineData("C:\\Users\\Joao\\..\\Other\\x")]
    [InlineData("C:\\Users\\JoaoOther\\x")]
    [InlineData("relative\\x")]
    public void Dotfiles_to_outside_profile_rejected(string to) =>
        AssertError(
            Dotfiles($$""" "path": "D:\\d", "files": [ { "from": "x", "to": "{{J(to)}}" } ] """),
            "modules.dev.dotfiles.files[0].to");

    [Theory]
    [InlineData("https://joao:ghp_token@github.com/joao/d.git")]
    [InlineData("https://joao@github.com/joao/d.git")]
    public void Dotfiles_repo_with_credentials_rejected(string repo) =>
        AssertError(Dotfiles($$""" "repo": "{{repo}}", {{File}} """), "modules.dev.dotfiles.repo");

    [Fact]
    public void Dotfiles_repo_with_at_sign_in_path_is_valid() =>
        Assert.Equal("https://github.com/joao/d@2.git", Valid(Dotfiles($$""" "repo": "https://github.com/joao/d@2.git", {{File}} """)).Dotfiles!.Repo);

    [Theory]
    [InlineData("relative\\dotfiles")]
    [InlineData("\\\\server\\share\\dotfiles")]
    [InlineData("\\dotfiles")]
    public void Dotfiles_cloneTo_must_be_a_full_local_path(string cloneTo) =>
        AssertError(Dotfiles($$""" "repo": "https://x.test/d.git", "cloneTo": "{{J(cloneTo)}}", {{File}} """), "modules.dev.dotfiles.cloneTo");

    [Fact]
    public void Dotfiles_cloneTo_is_normalized() =>
        Assert.Equal(
            @"C:\Users\Joao\b",
            Valid(Dotfiles($$""" "repo": "https://x.test/d.git", "cloneTo": "C:\\Users\\Joao\\a\\..\\b", {{File}} """)).Dotfiles!.CloneTo);

    [Fact]
    public void Dotfiles_to_is_stored_normalized()
    {
        var options = Valid(Dotfiles(""" "path": "D:\\d", "files": [ { "from": "x", "to": "%USERPROFILE%\\a\\..\\b\\.rc" } ] """));

        Assert.Equal(@"C:\Users\Joao\b\.rc", Assert.Single(options.Dotfiles!.Files).To);
    }

    [Fact]
    public void Dotfiles_duplicate_to_rejected_ignoring_case_and_normalization() =>
        AssertError(
            Dotfiles("""
                "path": "D:\\d",
                "files": [ { "from": "a", "to": "%USERPROFILE%\\.rc" }, { "from": "b", "to": "%USERPROFILE%\\x\\..\\.RC" } ]
                """),
            "modules.dev.dotfiles.files[1].to");

    private static string DotfileTo(string to, string others) =>
        $$"""{ "enabled": true, "dotfiles": { "path": "D:\\d", "files": [ { "from": "a", "to": "{{J(to)}}" } ] }, {{others}} }""";

    [Theory]
    [InlineData(@"""git"": { ""name"": ""A"" }", @"%USERPROFILE%\.gitconfig")]
    [InlineData(@"""ssh"": { }", @"%USERPROFILE%\x\..\.GITCONFIG")]
    [InlineData(@"""wsl"": { ""user"": ""joao"", ""wslconfig"": { ""memory"": ""8GB"" } }", @"%USERPROFILE%\.wslconfig")]
    [InlineData(@"""shell"": { ""windows"": { ""ohMyPosh"": true } }", @"%USERPROFILE%\Documents\PowerShell\Microsoft.PowerShell_profile.ps1")]
    [InlineData(@"""shell"": { ""windows"": { ""ohMyPosh"": true } }", @"%USERPROFILE%\documents\WindowsPowerShell\Microsoft.PowerShell_profile.ps1")]
    public void Dotfiles_target_owned_by_another_step_rejected(string others, string to) =>
        AssertError(DotfileTo(to, others), "modules.dev.dotfiles.files[0].to", ProfileErrorCode.InvalidValue);

    [Theory]
    [InlineData(@"""wsl"": { ""user"": ""joao"" }", @"%USERPROFILE%\.wslconfig")]
    [InlineData(@"""shell"": { ""windows"": { ""ohMyPosh"": false } }", @"%USERPROFILE%\Documents\PowerShell\Microsoft.PowerShell_profile.ps1")]
    [InlineData(@"""git"": { ""name"": ""A"" }", @"%USERPROFILE%\.gitconfig.local")]
    [InlineData(@"""shell"": { ""windows"": { ""ohMyPosh"": true } }", @"%USERPROFILE%\Documents\PowerShell\dotfiles.ps1")]
    public void Dotfiles_target_not_owned_is_allowed(string others, string to) =>
        Valid(DotfileTo(to, others));

    [Fact]
    public void Dotfiles_relative_path_resolves_against_profile()
    {
        var options = Valid(Dotfiles($$""" "path": "..\\dotfiles", {{File}} """), @"D:\profiles\main");

        Assert.Equal(@"D:\profiles\dotfiles", options.Dotfiles!.LocalPath);
        Assert.Null(options.Dotfiles.Repo);
    }

    [Fact]
    public void Dotfiles_needs_a_file()
    {
        AssertError(Dotfiles(""" "path": "D:\\d", "files": [] """), "modules.dev.dotfiles.files");
        AssertError(Dotfiles(""" "path": "D:\\d" """), "modules.dev.dotfiles.files", ProfileErrorCode.MissingField);
    }

    [Fact]
    public void Shell_wsl_requires_wsl() =>
        AssertError("""{ "enabled": true, "shell": { "wsl": { "zsh": true } } }""", "modules.dev.shell.wsl");

    [Fact]
    public void Shell_ohmyzsh_requires_zsh() =>
        AssertError(ShellWsl(""" "zsh": false, "ohMyZsh": true """), "modules.dev.shell.wsl.ohMyZsh");

    [Theory]
    [InlineData("someone/zsh-extra")]
    [InlineData("https://x.test/p.git")]
    [InlineData("a b")]
    public void Shell_unknown_external_plugin(string plugin) =>
        AssertError(ShellWsl($$""" "plugins": ["git", "{{plugin}}"] """), "modules.dev.shell.wsl.plugins[1]");

    [Fact]
    public void Shell_theme_pattern()
    {
        AssertError(ShellWsl(""" "theme": "a b" """), "modules.dev.shell.wsl.theme");
        AssertError(ShellWindows(""" "theme": "a;b" """), "modules.dev.shell.windows.theme");
    }

    [Theory]
    [InlineData("-x")]
    [InlineData("--help")]
    [InlineData(".")]
    [InlineData("..")]
    public void Shell_theme_and_plugin_reject_option_like_and_dot_names(string name)
    {
        AssertError(ShellWsl($$""" "theme": "{{name}}" """), "modules.dev.shell.wsl.theme");
        AssertError(ShellWindows($$""" "theme": "{{name}}" """), "modules.dev.shell.windows.theme");
        AssertError(ShellWsl($$""" "plugins": ["git", "{{name}}"] """), "modules.dev.shell.wsl.plugins[1]");
    }

    [Fact]
    public void Shell_plugins_are_deduplicated_keeping_first_order()
    {
        var options = Valid(ShellWsl(""" "plugins": ["git", "zsh-autosuggestions", "git", "z", "zsh-autosuggestions"] """));

        Assert.Equal(["git", "zsh-autosuggestions", "z"], options.Shell!.Wsl!.Plugins);
    }

    [Theory]
    [InlineData("Comic Sans")]
    [InlineData("meslo")]
    [InlineData("Iosevka")]
    public void Shell_font_not_in_allowlist(string font) =>
        AssertError(ShellWindows($$""" "font": "{{font}}" """), "modules.dev.shell.windows.font");

    [Fact]
    public void DevDrive_size_below_50() => AssertError(DriveWith(@"C:\d.vhdx", size: "49"), "modules.dev.devDrive.sizeGB");

    [Theory]
    [InlineData("C")]
    [InlineData("c")]
    [InlineData("DD")]
    [InlineData("1")]
    public void DevDrive_letter_C_rejected(string letter) =>
        AssertError(DriveWith(@"C:\d.vhdx", letter: letter), "modules.dev.devDrive.letter");

    [Theory]
    [InlineData("C:\\d.vhd")]
    [InlineData("d.vhdx")]
    [InlineData("\\\\server\\share\\d.vhdx")]
    [InlineData("C:d.vhdx")]
    public void DevDrive_path_must_be_vhdx(string path) => AssertError(DriveWith(path), "modules.dev.devDrive.path");

    [Theory]
    [InlineData(@"C:\.vhdx")]
    [InlineData(@"C:\ .vhdx")]
    [InlineData(@"C:/.vhdx")]
    [InlineData(@"C:\a*b.vhdx")]
    [InlineData(@"C:\a?b.vhdx")]
    [InlineData(@"C:\a<b.vhdx")]
    [InlineData(@"C:\a>b.vhdx")]
    [InlineData(@"C:\a|b.vhdx")]
    public void DevDrive_path_rejects_invalid_characters_and_bare_root_file(string path) =>
        AssertError(DriveWith(path), "modules.dev.devDrive.path");

    /// <summary>The JSON escapes <c>\"</c> and <c>\u0001</c> decode to a quote and a control character.</summary>
    [Theory]
    [InlineData(@"\""")]
    [InlineData(@"\u0001")]
    public void DevDrive_path_rejects_quote_and_control_characters(string jsonEscape)
    {
        var json = Drive($$""" "path": "{{J(@"C:\a")}}{{jsonEscape}}b.vhdx", "sizeGB": 50, "letter": "D" """);

        AssertError(json, "modules.dev.devDrive.path");
    }

    [Fact]
    public void DevDrive_name_may_start_with_a_dot_inside_a_folder() => Valid(DriveWith(@"C:\DevDrive\.dev.vhdx"));

    [Fact]
    public void DevDrive_vhdx_extension_ignores_case() => Valid(DriveWith(@"c:\DevDrive\DEV.VHDX"));

    [Theory]
    [InlineData("")]
    [InlineData("a;b")]
    public void DevDrive_label_pattern(string label) =>
        AssertError(Drive($$"""{{DriveOk}}, "label": "{{label}}" """), "modules.dev.devDrive.label");

    [Fact]
    public void DevDrive_unknown_cache() =>
        AssertError(Drive($$"""{{DriveOk}}, "caches": ["npm", "maven"] """), "modules.dev.devDrive.caches[1]");

    [Fact]
    public void Unknown_property_rejected()
    {
        AssertError("""{ "enabled": true, "docker": {} }""", "modules.dev.docker");
        AssertError(Wsl(""" "user": "joao", "color": 1 """), "modules.dev.wsl.color");
    }

    [Fact]
    public void Empty_enabled_section_valid()
    {
        var options = Valid("""{ "enabled": true }""");

        Assert.Null(options.Wsl);
        Assert.Null(options.Git);
        Assert.Null(options.Ssh);
        Assert.Null(options.Dotfiles);
        Assert.Null(options.Shell);
        Assert.Null(options.DevDrive);
    }

    [Fact]
    public void Non_object_section_rejected() => AssertError("[]", Path);

    [Fact]
    public void Subsection_must_be_an_object() => AssertError("""{ "enabled": true, "git": 3 }""", "modules.dev.git");
}
