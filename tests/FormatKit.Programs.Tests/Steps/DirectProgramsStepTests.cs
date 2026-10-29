using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Programs.Steps;

namespace FormatKit.Programs.Tests.Steps;

public class DirectProgramsStepTests : IDisposable
{
    private readonly System.Globalization.CultureInfo _original = System.Globalization.CultureInfo.CurrentUICulture;

    public DirectProgramsStepTests() => System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("en-US");

    public void Dispose() => System.Globalization.CultureInfo.CurrentUICulture = _original;

    private const string Installer = @"D:\setups\tool.exe";
    private static readonly AppPaths Paths = new(@"C:\FormatKitTest");

    private static (InMemoryFileSystem Files, FakeProcessRunner Runner, StepContext Ctx) Setup()
    {
        var files = new InMemoryFileSystem();
        var runner = new FakeProcessRunner();
        var ctx = TestServices.Context(TestServices.Create(processes: runner, files: files));
        files.Seed(Installer, "installer-bytes");
        return (files, runner, ctx);
    }

    private static DirectEntry Entry(string? sha = null) => new("Tool", null, Installer, sha, ["/S"]);

    [Fact]
    public async Task Done_key_is_skipped_as_already_installed()
    {
        var (files, runner, ctx) = Setup();
        files.Seed(Paths.ProgramsDirectFile, """["Tool|D:\\setups\\tool.exe"]""");

        var step = new DirectProgramsStep([Entry()], Paths);
        Assert.True(await step.CheckAsync(ctx, TestContext.Current.CancellationToken));
        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Contains("already installed", result.Messages[0]);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Success_marks_progress()
    {
        var (files, runner, ctx) = Setup();
        runner.Enqueue(Installer, new ProcessResult(0, "", "", TimeSpan.Zero, false));

        var step = new DirectProgramsStep([Entry()], Paths);
        Assert.False(await step.CheckAsync(ctx, TestContext.Current.CancellationToken));
        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Contains("Tool|D:", files.Files[Paths.ProgramsDirectFile]);
        Assert.True(await step.CheckAsync(ctx, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Hash_mismatch_fails_and_is_not_marked()
    {
        var (files, _, ctx) = Setup();
        var step = new DirectProgramsStep([Entry(new string('b', 64))], Paths);

        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains("hash", result.Messages[0]);
        Assert.False(files.FileExists(Paths.ProgramsDirectFile));
    }

    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        public List<Serilog.Events.LogEvent> Events { get; } = [];

        public void Emit(Serilog.Events.LogEvent logEvent) => Events.Add(logEvent);
    }

    private static (InMemoryFileSystem Files, FakeProcessRunner Runner, FakeHttpDownloader Http, CollectingSink Log, StepContext Ctx) SetupWithLog()
    {
        var files = new InMemoryFileSystem();
        var runner = new FakeProcessRunner();
        var http = new FakeHttpDownloader(files);
        var sink = new CollectingSink();
        var services = TestServices.Create(processes: runner, files: files, direct: new DirectInstaller(http, runner, files, Paths));
        var ctx = new StepContext(services, new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger());
        files.Seed(Installer, "installer-bytes");
        return (files, runner, http, sink, ctx);
    }

    private static string Rendered(CollectingSink sink) =>
        string.Join("\n", sink.Events.Where(e => e.Level == Serilog.Events.LogEventLevel.Warning).Select(e => e.RenderMessage()));

    [Fact]
    public async Task Timed_out_installer_is_reported_as_timeout_and_logged()
    {
        var (_, runner, _, sink, ctx) = SetupWithLog();
        runner.Enqueue(Installer, new ProcessResult(-1, "", "", TimeSpan.Zero, true));

        var result = await new DirectProgramsStep([Entry()], Paths).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.StartsWith("Tool: ", result.Messages[0]);
        Assert.Contains("timed out", result.Messages[0]);
        Assert.DoesNotContain("0xFFFFFFFF", result.Messages[0]);
        Assert.Contains("timed out", Rendered(sink));
    }

    [Fact]
    public async Task Hash_mismatch_logs_expected_and_actual_hash()
    {
        var (_, _, _, sink, ctx) = SetupWithLog();
        var expected = new string('b', 64);

        await new DirectProgramsStep([Entry(expected)], Paths).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        var log = Rendered(sink);
        Assert.Contains(expected, log);
        Assert.Contains("got ", log);
        Assert.Contains("HashMismatch", log);
    }

    [Fact]
    public async Task Download_error_is_logged()
    {
        var (_, _, http, sink, ctx) = SetupWithLog();
        http.Throws = new HttpRequestException("503 Service Unavailable");
        var entry = new DirectEntry("Web", "https://example.com/setup.exe", null, new string('c', 64), []);

        var result = await new DirectProgramsStep([entry], Paths).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains("503 Service Unavailable", Rendered(sink));
    }

    [Fact]
    public async Task Failed_exit_code_is_logged()
    {
        var (_, runner, _, sink, ctx) = SetupWithLog();
        runner.Enqueue(Installer, new ProcessResult(1603, "", "", TimeSpan.Zero, false));

        await new DirectProgramsStep([Entry()], Paths).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Contains("1603", Rendered(sink));
    }

    [Fact]
    public async Task Success_logs_no_warning()
    {
        var (_, runner, _, sink, ctx) = SetupWithLog();
        runner.Enqueue(Installer, new ProcessResult(0, "", "", TimeSpan.Zero, false));

        await new DirectProgramsStep([Entry()], Paths).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Empty(Rendered(sink));
    }

    [Fact]
    public async Task Corrupt_progress_file_is_treated_as_empty()
    {
        var (files, runner, ctx) = Setup();
        files.Seed(Paths.ProgramsDirectFile, "{ not json");
        runner.Enqueue(Installer, new ProcessResult(0, "", "", TimeSpan.Zero, false));

        var step = new DirectProgramsStep([Entry()], Paths);
        Assert.False(await step.CheckAsync(ctx, TestContext.Current.CancellationToken));
        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Contains("Tool|", files.Files[Paths.ProgramsDirectFile]);
    }
}
