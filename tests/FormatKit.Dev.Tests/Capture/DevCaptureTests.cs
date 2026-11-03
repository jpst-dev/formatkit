using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FormatKit.Core.Capture;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Dev.Capture;
using Serilog.Core;
using static FormatKit.Dev.Tests.DevTestData;

namespace FormatKit.Dev.Tests.Capture;

public class DevCaptureTests
{
    private const string Git = @"C:\Git\git.exe";
    private const string Posh = @"C:\Posh\oh-my-posh.exe";
    private const string Ssh = @"C:\Users\Joao\.ssh";
    private const string Fonts = @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\Fonts";
    private const string Vhdx = @"E:\Dev\dev.vhdx";
    private const long GB = 1024L * 1024 * 1024;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Machine
    {
        public FakeProcessRunner Processes { get; } = new();
        public InMemoryFileSystem Files { get; } = new();
        public FakeToolLocator Tools { get; } = new();
        public FakeRegistry Registry { get; } = new();
        public FakeUserEnvironment Environment { get; } = new();
        public FakeVirtualDisks Disks { get; } = new();
        public FakeVolumes Volumes { get; } = new();

        public Machine()
        {
            Processes.Enqueue("wsl.exe", Exit(0));
        }

        public ServiceSet Services => TestServices.Create(
            processes: Processes, files: Files, tools: Tools, registry: Registry,
            userEnvironment: Environment, virtualDisks: Disks, volumes: Volumes);

        public Task<CaptureSection> Capture() => DevCapture.RunAsync(Services, Env(), 'C', Logger.None, Ct);
    }

    private static ProcessResult Exit(int code, string stdout = "") => new(code, stdout, "", TimeSpan.Zero, false);

    private static void Config(Machine m, string? name = null, string? email = null, string? branch = null, string? editor = null, string? crlf = null)
    {
        m.Tools.Add("git.exe", Git);
        foreach (var value in new[] { name, email, branch, editor, crlf })
            m.Processes.Enqueue(Git, value is null ? Exit(1) : Exit(0, value + "\n"));
    }

    private static string? Text(JsonNode? node) => node?.GetValue<string>();

    // --- git

    [Fact]
    public async Task Git_reads_the_five_keys_and_nothing_else()
    {
        var m = new Machine();
        Config(m, "João Silva", "joao@example.com", "main", "code --wait", "input");

        var git = (await m.Capture()).Value["git"]!;

        Assert.Equal("João Silva", Text(git["name"]));
        Assert.Equal("joao@example.com", Text(git["email"]));
        Assert.Equal("main", Text(git["defaultBranch"]));
        Assert.Equal("code --wait", Text(git["editor"]));
        Assert.Equal("input", Text(git["autocrlf"]));
        Assert.Equal(
            ["user.name", "user.email", "init.defaultBranch", "core.editor", "core.autocrlf"],
            m.Processes.Calls.Where(c => c.FileName == Git).Select(c => c.Arguments[^1]));
        Assert.All(m.Processes.Calls.Where(c => c.FileName == Git), c => Assert.Equal(["config", "--global", "--get"], c.Arguments.Take(3)));
    }

    [Fact]
    public async Task Git_missing_key_is_left_out()
    {
        var m = new Machine();
        Config(m, name: "Joao");

        var git = (await m.Capture()).Value["git"]!.AsObject();

        Assert.Single(git);
    }

    [Fact]
    public async Task Git_with_no_key_gives_no_part()
    {
        var m = new Machine();
        Config(m);

        Assert.Null((await m.Capture()).Value["git"]);
    }

    [Fact]
    public async Task Git_not_found_gives_a_note()
    {
        var section = await new Machine().Capture();

        Assert.Null(section.Value["git"]);
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.NotCaptured && n.Path == "modules.dev.git");
    }

    [Fact]
    public async Task Invalid_git_value_is_dropped_with_a_note()
    {
        var m = new Machine();
        Config(m, name: "Joao", branch: "bad branch");

        var section = await m.Capture();

        Assert.Null(section.Value["git"]!["defaultBranch"]);
        Assert.Equal("Joao", Text(section.Value["git"]!["name"]));
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.NotCaptured && n.Path == "modules.dev.git.defaultBranch");
    }

    // --- ssh

    [Fact]
    public async Task Ssh_prefers_ed25519()
    {
        var m = new Machine();
        m.Files.Seed($@"{Ssh}\id_ed25519.pub", "ssh-ed25519 AAAA joao@pc\n");
        m.Files.Seed($@"{Ssh}\id_rsa.pub", "ssh-rsa BBBB old@pc\n");

        var ssh = (await m.Capture()).Value["ssh"]!;

        Assert.Equal("ed25519", Text(ssh["keyType"]));
        Assert.Equal("joao@pc", Text(ssh["comment"]));
    }

    [Fact]
    public async Task Ssh_falls_back_to_rsa()
    {
        var m = new Machine();
        m.Files.Seed($@"{Ssh}\id_rsa.pub", "ssh-rsa BBBB old@pc");

        Assert.Equal("rsa", Text((await m.Capture()).Value["ssh"]!["keyType"]));
    }

    [Fact]
    public async Task Ssh_comment_outside_the_pattern_is_left_out()
    {
        var m = new Machine();
        m.Files.Seed($@"{Ssh}\id_ed25519.pub", "ssh-ed25519 AAAA my laptop key");

        var ssh = (await m.Capture()).Value["ssh"]!;

        Assert.Equal("ed25519", Text(ssh["keyType"]));
        Assert.Null(ssh["comment"]);
    }

    [Fact]
    public async Task No_ssh_key_gives_no_part()
    {
        var section = await new Machine().Capture();

        Assert.Null(section.Value["ssh"]);
        Assert.DoesNotContain(section.Notes, n => n.Path.StartsWith("modules.dev.ssh", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ssh_note_says_the_key_is_not_captured()
    {
        var m = new Machine();
        m.Files.Seed($@"{Ssh}\id_ed25519.pub", "ssh-ed25519 AAAA joao@pc");

        var section = await m.Capture();

        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.Review && n.Path == "modules.dev.ssh");
    }

    [Fact]
    public async Task Never_opens_a_private_key()
    {
        var m = new Machine();
        m.Files.Seed($@"{Ssh}\id_ed25519", "PRIVATE");
        m.Files.Seed($@"{Ssh}\id_ed25519.pub", "ssh-ed25519 AAAA joao@pc");
        m.Files.Seed($@"{Ssh}\id_rsa", "PRIVATE");
        var opened = new List<string>();
        var services = m.Services with { Files = RecordingFiles.Wrap(m.Files, opened) };

        await DevCapture.RunAsync(services, Env(), 'C', Logger.None, Ct);

        Assert.Contains($@"{Ssh}\id_ed25519.pub", opened);
        Assert.DoesNotContain(opened, path => path.Contains(@"\.ssh\", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".pub", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Records every path the code reads or probes, without listing the whole file system interface.</summary>
    private class RecordingFiles : DispatchProxy
    {
        private IFileSystem _inner = null!;
        private List<string> _opened = null!;

        public static IFileSystem Wrap(IFileSystem inner, List<string> opened)
        {
            var proxy = Create<IFileSystem, RecordingFiles>();
            ((RecordingFiles)(object)proxy)._inner = inner;
            ((RecordingFiles)(object)proxy)._opened = opened;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name.StartsWith("Read", StringComparison.Ordinal) || method.Name.StartsWith("Open", StringComparison.Ordinal))
                _opened.AddRange(args!.OfType<string>().Take(1));
            try
            {
                return method.Invoke(_inner, args);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException!;
            }
        }
    }

    // --- wsl

    private static void Wsl(Machine m, string distros, string user = "joao\n", string shell = "")
    {
        // The constructor queued an empty distro list for the machines that have no WSL; replace it.
        m.Processes.Drain("wsl.exe");
        m.Processes.Enqueue("wsl.exe", Exit(0, distros));
        m.Processes.Enqueue("wsl.exe", Exit(0, user));
        m.Processes.Enqueue("wsl.exe", Exit(0, shell));
    }

    [Fact]
    public async Task First_allowed_distro_wins_and_others_get_a_note()
    {
        var m = new Machine();
        Wsl(m, "Fedora\nUbuntu-22.04\nUbuntu\n");

        var section = await m.Capture();

        Assert.Equal("Ubuntu", Text(section.Value["wsl"]!["distro"]));
        Assert.Equal("joao", Text(section.Value["wsl"]!["user"]));
        var note = Assert.Single(section.Notes, n => n.Path == "modules.dev.wsl");
        Assert.Contains("Fedora", note.Text);
        Assert.Contains("Ubuntu-22.04", note.Text);
    }

    [Fact]
    public async Task No_distro_gives_no_part()
    {
        var section = await new Machine().Capture();

        Assert.Null(section.Value["wsl"]);
        Assert.DoesNotContain(section.Notes, n => n.Path.StartsWith("modules.dev.wsl", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Wsl_not_installed_gives_no_part()
    {
        var m = new Machine();
        m.Processes.Drain("wsl.exe");
        m.Processes.ThrowNotFound("wsl.exe");

        Assert.Null((await m.Capture()).Value["wsl"]);
    }

    [Fact]
    public async Task Whoami_failure_gives_a_note()
    {
        var m = new Machine();
        m.Processes.Drain("wsl.exe");
        m.Processes.Enqueue("wsl.exe", Exit(0, "Ubuntu\n"));
        m.Processes.Enqueue("wsl.exe", Exit(1));

        var section = await m.Capture();

        Assert.Null(section.Value["wsl"]);
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.NotCaptured && n.Path == "modules.dev.wsl");
    }

    [Fact]
    public async Task Root_user_is_dropped_with_a_note()
    {
        var m = new Machine();
        Wsl(m, "Ubuntu\n", user: "root\n", shell: "zsh=1\n");

        var section = await m.Capture();

        Assert.Null(section.Value["wsl"]);
        Assert.Null(section.Value["shell"]);
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.NotCaptured && n.Path.StartsWith("modules.dev.wsl", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reads_zsh_ohmyzsh_theme_and_plugins()
    {
        var m = new Machine();
        Wsl(m, "Ubuntu\n", shell: "zsh=1\nomz=1\nZSH_THEME=\"agnoster\"\nplugins=(git docker)\n");

        var shell = (await m.Capture()).Value["shell"]!["wsl"]!;

        Assert.True(shell["zsh"]!.GetValue<bool>());
        Assert.True(shell["ohMyZsh"]!.GetValue<bool>());
        Assert.Equal("agnoster", Text(shell["theme"]));
        Assert.Equal(["git", "docker"], shell["plugins"]!.AsArray().Select(n => Text(n)));
    }

    [Fact]
    public async Task Reads_plugins_over_several_lines()
    {
        var m = new Machine();
        Wsl(m, "Ubuntu\n", shell: "zsh=1\nomz=1\nplugins=(\n  git\n  docker\n)\n");

        var shell = (await m.Capture()).Value["shell"]!["wsl"]!;

        Assert.Equal(["git", "docker"], shell["plugins"]!.AsArray().Select(n => Text(n)));
    }

    [Fact]
    public async Task No_zsh_gives_no_wsl_shell()
    {
        var m = new Machine();
        Wsl(m, "Ubuntu\n", shell: "\n");

        Assert.Null((await m.Capture()).Value["shell"]);
    }

    [Fact]
    public async Task Theme_with_a_folder_is_dropped_with_a_note()
    {
        var m = new Machine();
        Wsl(m, "Ubuntu\n", shell: "zsh=1\nomz=1\nZSH_THEME=\"powerlevel10k/powerlevel10k\"\n");

        var section = await m.Capture();

        Assert.Null(section.Value["shell"]!["wsl"]!["theme"]);
        Assert.Contains(section.Notes, n => n.Path == "modules.dev.shell.wsl.theme");
    }

    [Fact]
    public async Task Reads_wslconfig()
    {
        var m = new Machine();
        Wsl(m, "Ubuntu\n");
        m.Files.Seed(@"C:\Users\Joao\.wslconfig", "# mine\n[wsl2]\nMemory = 8GB\nprocessors=4\nswap=2GB\n[experimental]\nmemory=1GB\n");

        var config = (await m.Capture()).Value["wsl"]!["wslconfig"]!;

        Assert.Equal("8GB", Text(config["memory"]));
        Assert.Equal(4, config["processors"]!.GetValue<int>());
        Assert.Equal("2GB", Text(config["swap"]));
    }

    [Fact]
    public async Task Invalid_wslconfig_value_is_dropped_with_a_note()
    {
        var m = new Machine();
        Wsl(m, "Ubuntu\n");
        m.Files.Seed(@"C:\Users\Joao\.wslconfig", "[wsl2]\nmemory=8GB\nswap=0\nprocessors=999\n");

        var section = await m.Capture();

        var config = section.Value["wsl"]!["wslconfig"]!.AsObject();
        Assert.Single(config);
        Assert.Contains(section.Notes, n => n.Path == "modules.dev.wsl.wslconfig.swap");
        Assert.Contains(section.Notes, n => n.Path == "modules.dev.wsl.wslconfig.processors");
    }

    // --- Windows shell

    private static void PoshProfile(Machine m, string line, string folder = "PowerShell")
    {
        m.Tools.Add("oh-my-posh.exe", Posh);
        m.Files.Seed($@"C:\Users\Joao\Documents\{folder}\Microsoft.PowerShell_profile.ps1", $"# top\n{line}\n");
    }

    private static JsonNode? Windows(CaptureSection s) => s.Value["shell"]?["windows"];

    [Fact]
    public async Task Oh_my_posh_needs_the_exe_and_an_init_line()
    {
        var noExe = new Machine();
        noExe.Files.Seed(@"C:\Users\Joao\Documents\PowerShell\Microsoft.PowerShell_profile.ps1", "oh-my-posh init pwsh | Invoke-Expression");
        var noLine = new Machine();
        noLine.Tools.Add("oh-my-posh.exe", Posh);
        var commented = new Machine();
        PoshProfile(commented, "# oh-my-posh init pwsh | Invoke-Expression");

        Assert.Null(Windows(await noExe.Capture()));
        Assert.Null(Windows(await noLine.Capture()));
        Assert.Null(Windows(await commented.Capture()));
    }

    [Theory]
    [InlineData("oh-my-posh init pwsh --config paradox | Invoke-Expression", "paradox")]
    [InlineData("oh-my-posh init pwsh --config 'jandedobbeleer' | Invoke-Expression", "jandedobbeleer")]
    public async Task Theme_from_plain_name(string line, string theme)
    {
        var m = new Machine();
        PoshProfile(m, line);

        Assert.Equal(theme, Text(Windows(await m.Capture())!["theme"]));
    }

    [Theory]
    [InlineData("oh-my-posh init pwsh --config \"$env:POSH_THEMES_PATH\\paradox.omp.json\" | Invoke-Expression")]
    [InlineData("oh-my-posh init pwsh --config https://example.com/themes/paradox.omp.json | Invoke-Expression")]
    public async Task Theme_from_a_path_or_url_is_reduced_and_noted(string line)
    {
        var m = new Machine();
        PoshProfile(m, line);

        var section = await m.Capture();

        Assert.Equal("paradox", Text(Windows(section)!["theme"]));
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.Review && n.Path == "modules.dev.shell.windows.theme");
    }

    [Fact]
    public async Task Reads_windows_powershell_profile_too()
    {
        var m = new Machine();
        PoshProfile(m, "oh-my-posh init powershell | Invoke-Expression", "WindowsPowerShell");

        Assert.True(Windows(await m.Capture())!["ohMyPosh"]!.GetValue<bool>());
    }

    [Fact]
    public async Task First_installed_known_font()
    {
        var m = new Machine();
        PoshProfile(m, "oh-my-posh init pwsh | Invoke-Expression");
        m.Registry.Set(Fonts, "Hack Nerd Font Regular (TrueType)", RegistryKind.String, "x.ttf");
        m.Registry.Set(Fonts, "FiraCode Nerd Font Regular (TrueType)", RegistryKind.String, "y.ttf");

        Assert.Equal("FiraCode", Text(Windows(await m.Capture())!["font"]));
    }

    // --- Dev Drive

    private static Machine Drive(long? size = 100 * GB, char letter = 'D', string label = "Dev", string vhdx = Vhdx)
    {
        var m = new Machine();
        m.Volumes.Volumes.Add(new VolumeInfo(letter, 3, default, vhdx, null));
        m.Disks.States[vhdx] = new VirtualDiskState(true, letter, label, size);
        return m;
    }

    [Fact]
    public async Task Dev_drive_reads_path_letter_label_size()
    {
        var drive = (await Drive().Capture()).Value["devDrive"]!;

        Assert.Equal(Vhdx, Text(drive["path"]));
        Assert.Equal("D", Text(drive["letter"]));
        Assert.Equal("Dev", Text(drive["label"]));
        Assert.Equal(100, drive["sizeGB"]!.GetValue<int>());
    }

    [Fact]
    public async Task Dev_drive_size_rounds_down() =>
        Assert.Equal(100, (await Drive(100 * GB + GB - 1).Capture()).Value["devDrive"]!["sizeGB"]!.GetValue<int>());

    [Fact]
    public async Task Vhdx_on_the_windows_drive_gets_a_note()
    {
        var section = await Drive(vhdx: @"C:\Dev\dev.vhdx").Capture();

        Assert.Null(section.Value["devDrive"]);
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.NotCaptured && n.Path == "modules.dev.devDrive");
    }

    [Fact]
    public async Task Under_50GB_gets_a_note()
    {
        var section = await Drive(49 * GB).Capture();

        Assert.Null(section.Value["devDrive"]);
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.NotCaptured && n.Path.StartsWith("modules.dev.devDrive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_size_gets_a_note()
    {
        var section = await Drive(size: null).Capture();

        Assert.Null(section.Value["devDrive"]);
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.NotCaptured && n.Path == "modules.dev.devDrive");
    }

    [Fact]
    public async Task Caches_from_env_vars_pointing_into_the_drive()
    {
        var m = Drive();
        m.Environment.Set("npm_config_cache", @"D:\cache\npm");
        m.Environment.Set("PIP_CACHE_DIR", @"d:\cache\pip");
        m.Environment.Set("NUGET_PACKAGES", @"C:\elsewhere");

        var caches = (await m.Capture()).Value["devDrive"]!["caches"]!.AsArray().Select(n => Text(n));

        Assert.Equal(["npm", "pip"], caches.OrderBy(c => c));
    }

    [Fact]
    public async Task No_vhdx_gives_no_part()
    {
        var m = new Machine();
        m.Volumes.Volumes.Add(new VolumeInfo('C', 0, default, null, null));

        var section = await m.Capture();

        Assert.Null(section.Value["devDrive"]);
        Assert.DoesNotContain(section.Notes, n => n.Path.StartsWith("modules.dev.devDrive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Volumes_failure_gets_a_note()
    {
        var m = new Machine();
        m.Volumes.Throw = new InvalidOperationException("no");

        var section = await m.Capture();

        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.NotCaptured && n.Path == "modules.dev.devDrive");
    }

    // --- the section

    [Fact]
    public async Task Dotfiles_are_reported_not_captured()
    {
        var section = await new Machine().Capture();

        Assert.Null(section.Value["dotfiles"]);
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.NotCaptured && n.Path == "modules.dev.dotfiles");
    }

    [Fact]
    public async Task Nothing_found_disables_the_section()
    {
        var section = await new Machine().Capture();

        Assert.False(section.Value["enabled"]!.GetValue<bool>());
        Assert.Single(section.Value);
    }

    [Fact]
    public async Task Full_capture_passes_DevOptions_Parse()
    {
        var m = Drive();
        Config(m, "João", "joao@example.com", "main", "code --wait", "true");
        Wsl(m, "Ubuntu\n", shell: "zsh=1\nomz=1\nZSH_THEME=\"agnoster\"\nplugins=(git)\n");
        m.Files.Seed($@"{Ssh}\id_ed25519.pub", "ssh-ed25519 AAAA joao@pc");
        m.Files.Seed(@"C:\Users\Joao\.wslconfig", "[wsl2]\nmemory=8GB\n");
        PoshProfile(m, "oh-my-posh init pwsh --config paradox | Invoke-Expression");
        m.Registry.Set(Fonts, "MesloLGM Nerd Font Regular (TrueType)", RegistryKind.String, "x.ttf");

        var section = await m.Capture();

        Assert.True(section.Value["enabled"]!.GetValue<bool>());
        foreach (var part in new[] { "git", "ssh", "wsl", "shell", "devDrive" })
            Assert.NotNull(section.Value[part]);
        using var doc = JsonDocument.Parse(section.Value.ToJsonString());
        var (_, errors) = DevOptions.Parse(doc.RootElement, "modules.dev", null, Env());
        Assert.Empty(errors);
    }
}
