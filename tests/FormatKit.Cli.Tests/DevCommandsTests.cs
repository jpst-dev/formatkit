using System.Globalization;
using FormatKit.Cli.Tests.Fakes;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Dev;

namespace FormatKit.Cli.Tests;

[Collection("Culture")]
public sealed class DevCommandsTests : IDisposable
{
    private const string Keygen = @"C:\Windows\System32\OpenSSH\ssh-keygen.exe";

    private readonly CultureInfo _original = CultureInfo.CurrentUICulture;
    private readonly string _tempDir = Directory.CreateTempSubdirectory("formatkit-cli-dev-").FullName;
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeElevation _elevation = new() { IsElevated = true };
    private readonly FakeProcessRunner _processes = new();
    private readonly FakeInteractiveConsole _console = new();
    private readonly FakeToolLocator _tools = new();
    private readonly FakeServiceControl _serviceControl = new();
    private readonly StringWriter _out = new();
    private string _userName = "Alice";

    public DevCommandsTests() => CultureInfo.CurrentUICulture = new CultureInfo("en-US");

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _original;
        Directory.Delete(_tempDir, recursive: true);
    }

    private CliApp App()
    {
        var services = TestServices.Create(
            processes: _processes, files: _fs, console: _console, tools: _tools, services: _serviceControl);
        var dev = new DevEnvironment(
            @"C:\Users\Alice", @"C:\Users\Alice\Documents", _userName, "NEW-PC", 8, s => s);
        var env = new CliEnvironment(
            services, _elevation, new FakeScheduledTasks(), new FakeRebooter(), new FakeReportWriter(),
            new RunStateStore(_fs, new AppPaths("data")), Serilog.Core.Logger.None, _out, TimeSpan.Zero, TimeSpan.Zero,
            Dev: dev);
        return new CliApp(env);
    }

    private Task<int> Run(params string[] args) => App().RunAsync(args, CancellationToken.None);

    private static ProcessResult Result(int exit, string stdout = "") => new(exit, stdout, "", TimeSpan.Zero, false);

    private string WriteProfile(string dev)
    {
        var path = Path.Combine(_tempDir, "dev.json");
        File.WriteAllText(path, """{"schemaVersion":1,"name":"T","modules":{"dev":""" + dev + "}}");
        return path;
    }

    [Fact]
    public void Dev_module_registered_after_programs()
    {
        Assert.Equal(["drivers", "cleanup", "programs", "dev"], App().Modules.Select(m => m.Name));
    }

    [Fact]
    public async Task WslUser_redirected_console_exit_2()
    {
        _console.CanPrompt = false;

        var exit = await Run("dev", "wsl-user");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("needs a console", _out.ToString());
        Assert.Empty(_processes.Calls);
    }

    [Fact]
    public async Task WslUser_without_profile_uses_windows_user()
    {
        _processes.Enqueue("wsl.exe", Result(0, "1000"));                              // id -u
        _processes.Enqueue("wsl.exe", Result(0, "alice P 2026-10-29 0 99999 7 -1"));   // passwd -S
        _processes.Enqueue("wsl.exe", Result(0, "[user]\ndefault=alice\n"));           // cat /etc/wsl.conf

        var exit = await Run("dev", "wsl-user");

        Assert.Equal(ExitCodes.Ok, exit);
        var first = Assert.IsType<ProcessSpec>(_processes.Calls[0]);
        Assert.Contains("Ubuntu", first.Arguments);
        Assert.Contains("alice", first.Arguments);
        Assert.Contains("already in order", _out.ToString());
    }

    [Fact]
    public async Task WslUser_unusable_windows_user_name_exit_2()
    {
        _userName = "João Silva";

        var exit = await Run("dev", "wsl-user");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("--profile", _out.ToString());
        Assert.Empty(_processes.Calls);
    }

    [Fact]
    public async Task SshKey_reads_profile_ssh_section()
    {
        _tools.Add("ssh-keygen.exe", Keygen);
        _serviceControl.Services["ssh-agent"] = ServiceStartType.Automatic;
        _processes.Enqueue("sc.exe", Result(1056));
        var profile = WriteProfile("""{"enabled":true,"ssh":{"keyType":"rsa","comment":"me@box"}}""");

        var exit = await Run("dev", "ssh-key", "--profile", profile);

        Assert.Equal(ExitCodes.Ok, exit);
        var attached = Assert.Single(_console.Attached);
        Assert.Equal(["-t", "rsa", "-b", "4096", "-C", "me@box", "-f", @"C:\Users\Alice\.ssh\id_rsa"], attached.Arguments);
    }

    [Fact]
    public async Task SshKey_invalid_profile_exit_2()
    {
        var profile = WriteProfile("""{"enabled":true,"ssh":{"keyType":"dsa"}}""");

        var exit = await Run("dev", "ssh-key", "--profile", profile);

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Empty(_console.Attached);
    }

    [Fact]
    public async Task SshKey_relaunches_elevated_when_not_elevated()
    {
        _elevation.IsElevated = false;
        _elevation.RelaunchResult = ExitCodes.Ok;

        var exit = await Run("dev", "ssh-key");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal(1, _elevation.RelaunchCalls);
        Assert.Empty(_console.Attached);
    }

    [Fact]
    public async Task Dev_commands_exit_1_on_pending()
    {
        // wsl-user: the user cannot be created, so the item fails.
        _processes.Enqueue("wsl.exe", Result(1));  // id -u
        _processes.Enqueue("wsl.exe", Result(1));  // useradd
        var wsl = await Run("dev", "wsl-user");

        // ssh-key: ssh-keygen exits non-zero, so the key item fails.
        _tools.Add("ssh-keygen.exe", Keygen);
        _serviceControl.Services["ssh-agent"] = ServiceStartType.Automatic;
        _processes.Enqueue("sc.exe", Result(1056));
        _console.ExitCode = _ => 1;
        var ssh = await Run("dev", "ssh-key");

        Assert.Equal(ExitCodes.CompletedWithFailures, wsl);
        Assert.Equal(ExitCodes.CompletedWithFailures, ssh);
        Assert.Contains("failed", _out.ToString());
        Assert.Contains("next:", _out.ToString());
    }
}
