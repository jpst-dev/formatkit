using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FormatKit.Cli.Commands;
using FormatKit.Cli.Tests.Fakes;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Drivers.Catalog;
using FormatKit.Hosting;
using Serilog.Core;

namespace FormatKit.Cli.Tests;

[Collection("Culture")]
public sealed class UpdateCommandTests : IDisposable
{
    private const string Exe = @"C:\Tools\formatkit.exe";
    private const string Download = "https://github.com/jpst-dev/formatkit/releases/download/v0.3.0/";

    private readonly CultureInfo _original = CultureInfo.CurrentUICulture;
    private readonly AppPaths _paths = new(@"C:\data\FormatKit");
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeInteractiveConsole _console = new();
    private readonly StringWriter _out = new();
    private readonly FakeSource _source = new();
    private readonly FakeHttpDownloader _downloader;
    private string _version = "0.2.1+abc";
    private string _exe = Exe;
    private string? _answer = "";
    private int _prompts;

    private sealed class FakeSource : IReleaseSource
    {
        public Exception? Throws { get; set; }

        public string Tag { get; set; } = "v0.3.0";

        public Task<ReleaseInfo> GetLatestAsync(CancellationToken ct) => Throws is not null
            ? Task.FromException<ReleaseInfo>(Throws)
            : Task.FromResult(new ReleaseInfo(
                Tag,
                new Uri("https://github.com/jpst-dev/formatkit/releases/tag/v0.3.0"),
                [
                    new ReleaseAsset("formatkit.exe", new Uri(Download + "formatkit.exe"), 3),
                    new ReleaseAsset("SHA256SUMS.txt", new Uri(Download + "SHA256SUMS.txt"), 1),
                ]));
    }

    public UpdateCommandTests()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("new"))).ToLowerInvariant();
        _downloader = new FakeHttpDownloader(_fs)
        {
            ContentFor = url => url.Segments[^1] == "SHA256SUMS.txt" ? $"{hash}  formatkit.exe\n" : "new",
        };
        _fs.Seed(Exe, "old");
        MediaCommands.ReadLine = () =>
        {
            _prompts++;
            return _answer;
        };
        UpdateCommand.CreateUpdater = context => new Updater(
            _source, _downloader, context.Env.Services.Files, context.Env.StateStore, _exe, _version, Logger.None);
    }

    public void Dispose()
    {
        MediaCommands.ReadLine = Console.ReadLine;
        UpdateCommand.CreateUpdater = UpdateCommand.CreateRealUpdater;
        CultureInfo.CurrentUICulture = _original;
    }

    private Task<int> Run(params string[] args)
    {
        var services = TestServices.Create(files: _fs, console: _console);
        var env = new CliEnvironment(
            services, new FakeElevation(), new FakeScheduledTasks(), new FakeRebooter(), new FakeReportWriter(),
            new RunStateStore(_fs, _paths), Logger.None, _out, TimeSpan.Zero, TimeSpan.Zero, null);
        return new CliApp(env, VendorCatalog.LoadEmbedded(), []).RunAsync(["update", .. args], CancellationToken.None);
    }

    [Fact]
    public async Task Check_shows_the_versions_and_changes_nothing()
    {
        var exit = await Run("--check");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains("0.2.1", _out.ToString());
        Assert.Contains("0.3.0", _out.ToString());
        Assert.Contains("releases/tag/v0.3.0", _out.ToString());
        Assert.Equal("old", _fs.ReadAllText(Exe));
        Assert.Equal(0, _prompts);
    }

    [Fact]
    public async Task Up_to_date_says_so_and_does_not_prompt()
    {
        _source.Tag = "v0.2.1";

        var exit = await Run();

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains("up to date", _out.ToString());
        Assert.Equal(0, _prompts);
    }

    [Theory]
    [InlineData("y", "new")]
    [InlineData("Y", "new")]
    [InlineData("s", "new")]
    [InlineData("n", "old")]
    [InlineData("", "old")]
    [InlineData(null, "old")]
    public async Task Asks_before_updating(string? answer, string expected)
    {
        _answer = answer;

        var exit = await Run();

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal(1, _prompts);
        Assert.Equal(expected, _fs.ReadAllText(Exe));
    }

    [Fact]
    public async Task Yes_updates_without_asking()
    {
        var exit = await Run("--yes");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal(0, _prompts);
        Assert.Equal("new", _fs.ReadAllText(Exe));
        Assert.Contains("0.3.0", _out.ToString());
    }

    [Fact]
    public async Task No_console_and_no_yes_says_how_and_fails()
    {
        _console.CanPrompt = false;

        var exit = await Run();

        Assert.Equal(ExitCodes.CompletedWithFailures, exit);
        Assert.Contains("--yes", _out.ToString());
        Assert.Equal("old", _fs.ReadAllText(Exe));
    }

    [Fact]
    public async Task A_failed_check_gives_the_reason_and_fails()
    {
        _source.Throws = new HttpRequestException("offline");

        var exit = await Run();

        Assert.Equal(ExitCodes.CompletedWithFailures, exit);
        Assert.Contains("offline", _out.ToString());
    }

    [Fact]
    public async Task A_failed_check_with_check_only_fails_too()
    {
        _source.Throws = new TimeoutException("slow");

        Assert.Equal(ExitCodes.CompletedWithFailures, await Run("--check"));
    }

    [Fact]
    public async Task A_failed_update_says_nothing_changed_and_fails()
    {
        _downloader.Throws = new HttpRequestException("offline");

        var exit = await Run("--yes");

        Assert.Equal(ExitCodes.CompletedWithFailures, exit);
        Assert.Contains("offline", _out.ToString());
        Assert.Equal("old", _fs.ReadAllText(Exe));
    }

    [Fact]
    public async Task A_pending_run_refuses_with_invalid_input()
    {
        new RunStateStore(_fs, _paths).Save(new RunState("p.json", "hash", ["a"], [], DateTimeOffset.UnixEpoch));

        var exit = await Run("--yes");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Equal("old", _fs.ReadAllText(Exe));
    }

    [Fact]
    public async Task Another_file_name_refuses_with_invalid_input()
    {
        _exe = @"C:\dev\FormatKit.Cli.exe";

        Assert.Equal(ExitCodes.InvalidInput, await Run("--yes"));
    }

    [Fact]
    public async Task Check_still_works_from_another_file_name()
    {
        _exe = @"C:\dev\FormatKit.Cli.exe";

        Assert.Equal(ExitCodes.Ok, await Run("--check"));
    }
}
