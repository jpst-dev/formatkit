using System.Globalization;
using System.Text.Json;
using FormatKit.Cli.Tests.Fakes;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Programs.Catalog;

namespace FormatKit.Cli.Tests;

[Collection("Culture")]
public sealed class ProgramsCommandsTests : IDisposable
{
    private readonly CultureInfo _original = CultureInfo.CurrentUICulture;
    private readonly string _tempDir = Directory.CreateTempSubdirectory("formatkit-cli-programs-").FullName;
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeElevation _elevation = new();
    private readonly FakeProcessRunner _processes = new();
    private readonly StringWriter _out = new();
    private readonly ProgramCatalog _catalog = ProgramCatalog.LoadEmbedded();

    public ProgramsCommandsTests() => CultureInfo.CurrentUICulture = new CultureInfo("en-US");

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _original;
        Directory.Delete(_tempDir, recursive: true);
    }

    private CliApp App()
    {
        var services = TestServices.Create(processes: _processes, files: _fs);
        var env = new CliEnvironment(
            services, _elevation, new FakeScheduledTasks(), new FakeRebooter(), new FakeReportWriter(),
            new RunStateStore(_fs, new AppPaths("data")), Serilog.Core.Logger.None, _out, TimeSpan.Zero, TimeSpan.Zero);
        return new CliApp(env);
    }

    private Task<int> Run(params string[] args) => App().RunAsync(args, CancellationToken.None);

    private static ProcessResult Result(int exit, string stdout = "", string stderr = "") =>
        new(exit, stdout, stderr, TimeSpan.Zero, false);

    [Fact]
    public async Task List_prints_every_program()
    {
        var exit = await Run("programs", "list");

        Assert.Equal(ExitCodes.Ok, exit);
        var lines = _out.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(_catalog.Programs.Count, lines.Length);
        foreach (var program in _catalog.Programs)
        {
            var line = Assert.Single(lines, l => l.StartsWith(program.Id + " ", StringComparison.Ordinal));
            Assert.Contains(program.LocalizedText().Name, line);
            Assert.Equal(program.Context == InstallContext.User, line.EndsWith("[user]", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task List_filters_by_category()
    {
        var exit = await Run("programs", "list", "--category", "browsers");

        Assert.Equal(ExitCodes.Ok, exit);
        var output = _out.ToString();
        foreach (var program in _catalog.Programs)
            Assert.Equal(program.Category == ProgramCategory.Browsers, output.Contains(program.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_verbose_adds_description_and_winget_id()
    {
        var exit = await Run("programs", "list", "--category", "browsers", "--verbose");

        Assert.Equal(ExitCodes.Ok, exit);
        var chrome = _catalog.Find("browsers.chrome")!;
        Assert.Contains("    " + chrome.LocalizedText().Description, _out.ToString());
        Assert.Contains($"    winget: {chrome.WingetId} ({chrome.Source})", _out.ToString());
    }

    [Fact]
    public async Task List_invalid_category_exits_2()
    {
        var exit = await Run("programs", "list", "--category", "Browser");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("browsers, communication, games, media, utilities, productivity, development", _out.ToString());
    }

    [Fact]
    public async Task Search_passes_term_and_prints_output()
    {
        _processes.Enqueue("winget.exe", Result(0, "Name  Id\nVLC  VideoLAN.VLC\n"));

        var exit = await Run("programs", "search", "vlc player");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal("Name  Id\nVLC  VideoLAN.VLC\n", _out.ToString());
        var call = Assert.Single(_processes.Calls);
        Assert.Equal("winget.exe", call.FileName);
        Assert.Equal(["search", "--query", "vlc player"], call.Arguments.Take(3));
    }

    [Fact]
    public async Task Search_without_winget_exits_1()
    {
        _processes.ThrowNotFound("winget.exe");

        var exit = await Run("programs", "search", "vlc");

        Assert.Equal(ExitCodes.CompletedWithFailures, exit);
        Assert.Contains("Install or repair App Installer (winget)", _out.ToString());
    }

    [Fact]
    public async Task Export_writes_and_prints_usage()
    {
        _processes.Enqueue("winget.exe", Result(0));
        var file = Path.Combine(_tempDir, "apps.json");

        var exit = await Run("programs", "export", file);

        Assert.Equal(ExitCodes.Ok, exit);
        var call = Assert.Single(_processes.Calls);
        Assert.Equal(["export", "-o", file], call.Arguments.Take(3));
        Assert.Contains("\"importFile\": " + JsonSerializer.Serialize(file), _out.ToString());
    }

    [Fact]
    public async Task Export_resolves_a_relative_path()
    {
        _processes.Enqueue("winget.exe", Result(0));

        var exit = await Run("programs", "export", "apps.json");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal(Path.GetFullPath("apps.json"), _processes.Calls[0].Arguments[2]);
    }

    [Fact]
    public async Task Export_failure_exits_1()
    {
        _processes.Enqueue("winget.exe", Result(1, "boom"));

        var exit = await Run("programs", "export", Path.Combine(_tempDir, "apps.json"));

        Assert.Equal(ExitCodes.CompletedWithFailures, exit);
        Assert.DoesNotContain("importFile", _out.ToString());
    }

    [Fact]
    public async Task Dry_run_lists_programs_under_steps()
    {
        var profile = Path.Combine(_tempDir, "programs.json");
        File.WriteAllText(profile, """
            {"schemaVersion":1,"name":"T","modules":{"programs":{"enabled":true,"acceptAgreements":true,
              "catalog":["browsers.chrome","communication.discord"],
              "direct":[{"name":"Tool","url":"https://example.com/tool.exe","sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]}}}
            """);

        var exit = await Run("run", "--profile", profile, "--dry-run");

        Assert.Equal(ExitCodes.Ok, exit);
        var output = _out.ToString();
        var chrome = _catalog.Find("browsers.chrome")!;
        var discord = _catalog.Find("communication.discord")!;
        Assert.Contains("programs.browsers", output);
        Assert.Contains($"    browsers.chrome  {chrome.LocalizedText().Name}{Environment.NewLine}", output);
        Assert.Contains($"    communication.discord  {discord.LocalizedText().Name} [user]", output);
        Assert.Contains("    Tool (https://example.com/tool.exe)", output);
        Assert.Equal(0, _elevation.RelaunchCalls);
    }

    [Fact]
    public void Default_modules_order_is_drivers_cleanup_programs()
    {
        Assert.Equal(["drivers", "cleanup", "programs"], App().Modules.Select(m => m.Name));
    }

    [Fact]
    public async Task Commands_never_elevate()
    {
        _processes.Enqueue("winget.exe", Result(0, "x"));
        _processes.Enqueue("winget.exe", Result(0));

        await Run("programs", "list");
        await Run("programs", "search", "x");
        await Run("programs", "export", Path.Combine(_tempDir, "a.json"));

        Assert.Equal(0, _elevation.RelaunchCalls);
    }
}
