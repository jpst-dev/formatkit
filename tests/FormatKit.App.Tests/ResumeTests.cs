using FormatKit.App.Localization;
using FormatKit.App.Services;
using FormatKit.App.Tests.Fakes;
using FormatKit.App.ViewModels;
using FormatKit.App.Worker;
using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Hosting;

namespace FormatKit.App.Tests;

public sealed class ResumeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryFileSystem _files = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeWorkerLauncher _launcher = new();
    private readonly FakeAppElevation _elevation = new();
    private readonly RunStateStore _store;

    public ResumeTests()
    {
        _store = TestViewModels.Store(_files);
    }

    private (MainViewModel Main, HomeViewModel Home, RunProfileViewModel Run) Create()
    {
        IReadOnlyList<IModule> modules = [];
        var env = new HostEnvironment(
            TestServices.Create(files: _files), _elevation, new FakeScheduledTasks(), new FakeRebooter(),
            new FakeReportWriter(), _store, new Serilog.LoggerConfiguration().CreateLogger(), TimeSpan.Zero, TimeSpan.Zero);
        var run = new RunProfileViewModel(
            new ProfileService(modules), modules, _launcher, _elevation, _dialogs,
            new AppSettings(_files, @"C:\FormatKitTest\app.json"), env);
        var home = new HomeViewModel(_store);
        var main = TestViewModels.CreateMain(home, run);
        return (main, home, run);
    }

    private void SeedState() =>
        _store.Save(new RunState("p.json", "hash", ["drivers.scan"], [], DateTimeOffset.UnixEpoch));

    private static Finished FinishedWith(RunStatus status, string? error = null) =>
        new(new RunSummary(status, [], @"C:\reports\run-1", error, false));

    [Fact]
    public async Task ResumeMode_StartsResumeWithoutRunasWhenElevated()
    {
        _elevation.IsElevated = true;
        _launcher.Script.Add(new Planned([new PlannedStep("drivers.scan", "Scan drivers", true)]));
        _launcher.Script.Add(FinishedWith(RunStatus.Completed));
        var (main, _, run) = Create();

        main.Navigate(Page.RunProfile);
        await run.ResumeAsync().WaitAsync(Timeout, Ct);

        Assert.Equal([["run", "--resume"]], _launcher.Jobs);
        Assert.Equal([false], _launcher.Elevations);
        Assert.Equal(RunPhase.Result, run.Phase);
        Assert.Equal(["drivers.scan"], run.Steps.Select(s => s.Id));
    }

    [Fact]
    public async Task MainResume_OpensRunProfileAndResumes()
    {
        // A resume signalled to the open window by the resume task after a reboot.
        SeedState();
        _launcher.Script.Add(FinishedWith(RunStatus.Completed));
        var (main, _, run) = Create();
        main.Navigate(Page.About);

        await main.ResumeAsync().WaitAsync(Timeout, Ct);

        Assert.Equal(Page.RunProfile, main.Current);
        Assert.Equal([["run", "--resume"]], _launcher.Jobs);
        Assert.Equal(RunPhase.Result, run.Phase);
    }

    [Fact]
    public async Task HomeResume_NotElevated_UsesRunas()
    {
        SeedState();
        _launcher.Script.Add(FinishedWith(RunStatus.Completed));
        var (main, home, run) = Create();

        home.ResumeCommand.Execute(null);
        await WaitUntil(() => run.Phase == RunPhase.Result);

        Assert.Equal(Page.RunProfile, main.Current);
        Assert.Equal([["run", "--resume"]], _launcher.Jobs);
        Assert.Equal([true], _launcher.Elevations);
    }

    [Fact]
    public async Task Resume_UacDeclined_BackToChooseWithMessage()
    {
        _launcher.Declines = true;
        var (_, _, run) = Create();

        await run.ResumeAsync().WaitAsync(Timeout, Ct);

        Assert.Equal(RunPhase.Choose, run.Phase);
        Assert.Equal(Strings.Get("RunProfile_UacDeclined"), run.RunMessage);
        Assert.False(run.IsRunning);
        Assert.False(run.IsBusy);
    }

    [Fact]
    public async Task Resume_NothingToResume_ShowsCliText()
    {
        _launcher.Script.Add(FinishedWith(RunStatus.NothingToResume));
        var (_, _, run) = Create();

        await run.ResumeAsync().WaitAsync(Timeout, Ct);

        Assert.Equal(Strings.Get("Run_NothingToResume"), run.FinalStatus);
    }

    [Fact]
    public async Task Resume_InvalidProfile_ShowsCliProfileInvalidText()
    {
        _launcher.Script.Add(FinishedWith(RunStatus.NothingToResume, error: "bad"));
        var (_, _, run) = Create();

        await run.ResumeAsync().WaitAsync(Timeout, Ct);

        Assert.Equal(Strings.Get("Run_ResumeProfileInvalid"), run.FinalStatus);
    }

    [Fact]
    public void Home_RefreshesInterruptedRunWhenNavigationReturns()
    {
        var (main, home, _) = Create();
        Assert.False(home.HasInterruptedRun);
        main.Navigate(Page.About);

        SeedState();
        main.Navigate(Page.Home);

        Assert.True(home.HasInterruptedRun);
    }

    [Fact]
    public async Task Home_RefreshesInterruptedRunAfterARunEnds()
    {
        var (_, home, run) = Create();
        _launcher.Script.Add(new Do(SeedState));
        _launcher.Script.Add(FinishedWith(RunStatus.RebootPending));
        Assert.False(home.HasInterruptedRun);

        await run.ResumeAsync().WaitAsync(Timeout, Ct);

        Assert.True(home.HasInterruptedRun);
    }

    [Fact]
    public async Task ResultPage_ResumeCommand_ResumesTheStoppedRun()
    {
        // The worker's stop text says "Use Resume": the result page offers it.
        var (_, _, run) = Create();
        _launcher.Script.Add(new Do(SeedState));
        _launcher.Script.Add(new Failed(Strings.Get("Worker_Stopped"), FailureReason.Stopped));
        await run.ResumeAsync().WaitAsync(Timeout, Ct);
        Assert.Equal(RunPhase.Result, run.Phase);
        Assert.True(run.HasInterruptedRun);
        Assert.True(run.ResumeCommand.CanExecute(null));

        _launcher.Script.Clear();
        _launcher.Script.Add(FinishedWith(RunStatus.Completed));
        await run.ResumeCommand.ExecuteAsync(null).WaitAsync(Timeout, Ct);

        Assert.Equal([["run", "--resume"], ["run", "--resume"]], _launcher.Jobs);
        Assert.Equal(Strings.Get("Run_Status_Completed"), run.FinalStatus);
    }

    [Fact]
    public async Task ResultPage_ResumeCommand_UnavailableWithoutAnInterruptedRun()
    {
        var (_, _, run) = Create();
        _launcher.Script.Add(FinishedWith(RunStatus.Completed));
        await run.ResumeAsync().WaitAsync(Timeout, Ct);

        Assert.False(run.HasInterruptedRun);
        Assert.False(run.ResumeCommand.CanExecute(null));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not reached");
            await Task.Delay(10, Ct);
        }
    }
}
