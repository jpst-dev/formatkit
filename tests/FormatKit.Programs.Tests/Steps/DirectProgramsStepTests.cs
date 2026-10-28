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
