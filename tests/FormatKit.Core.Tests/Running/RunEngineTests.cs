using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.Running;

public class RunEngineTests
{
    private static readonly AppPaths Paths = new(@"C:\FormatKitTest");

    private sealed class Harness
    {
        public InMemoryFileSystem Fs { get; } = new();
        public RunStateStore Store { get; }
        public FakeScheduledTasks Tasks { get; } = new();
        public FakeRebooter Rebooter { get; } = new();
        public FakeReportWriter Reports { get; } = new();
        public RunEngine Engine { get; }

        public Harness()
        {
            Store = new RunStateStore(Fs, Paths);
            Engine = new RunEngine(Store, Tasks, Rebooter, Reports, TimeSpan.FromSeconds(60));
        }

        public static RunRequest Request(bool resume, params IStep[] plan) =>
            new("profile.json", "hash1", plan,
                new StepContext(null!, new Serilog.LoggerConfiguration().CreateLogger()), resume);
    }

    private static FakeStep Work(string id) => new(id) { CheckResult = false };

    [Fact]
    public async Task Runs_all_steps_and_completes()
    {
        var h = new Harness();
        var a = Work("a"); var b = Work("b");

        var summary = await h.Engine.RunAsync(Harness.Request(false, a, b), TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.Completed, summary.Status);
        Assert.Equal(1, a.ExecuteCalls);
        Assert.Equal(1, b.ExecuteCalls);
        Assert.Equal(StateLoadStatus.None, h.Store.Load().Status);
        Assert.Single(h.Reports.Written);
        Assert.Equal(FakeReportWriter.Directory, summary.ReportDirectory);
        Assert.Equal(1, h.Tasks.RemoveCalls);
        Assert.Equal(["a", "b"], summary.Steps.Select(s => s.Id));
    }

    [Fact]
    public async Task Step_already_done_is_skipped()
    {
        var h = new Harness();
        var a = new FakeStep("a") { CheckResult = true };

        var summary = await h.Engine.RunAsync(Harness.Request(false, a), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Skipped, summary.Steps[0].Outcome);
        Assert.Equal(0, a.ExecuteCalls);
        Assert.Equal(RunStatus.Completed, summary.Status);
    }

    [Fact]
    public async Task Non_critical_failure_continues()
    {
        var h = new Harness();
        var a = Work("a"); a.ExecuteResult = StepResult.Fail("fix it", "bad");
        var b = Work("b");

        var summary = await h.Engine.RunAsync(Harness.Request(false, a, b), TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.CompletedWithFailures, summary.Status);
        Assert.Equal(1, b.ExecuteCalls);
        Assert.Equal("fix it", summary.Steps[0].NextAction);
    }

    [Fact]
    public async Task Critical_failure_stops_run()
    {
        var h = new Harness();
        var a = Work("a"); a.IsCritical = true; a.ExecuteResult = StepResult.Fail(null, "bad");
        var b = Work("b");

        var summary = await h.Engine.RunAsync(Harness.Request(false, a, b), TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.StoppedOnCriticalFailure, summary.Status);
        Assert.Equal(0, b.ExecuteCalls);
        Assert.Single(h.Reports.Written);
        Assert.Single(summary.Steps);
    }

    [Fact]
    public async Task Exception_becomes_failed_step()
    {
        var h = new Harness();
        var a = Work("a"); a.Throws = new InvalidOperationException("boom");
        var b = Work("b");

        var summary = await h.Engine.RunAsync(Harness.Request(false, a, b), TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.CompletedWithFailures, summary.Status);
        Assert.Equal(StepOutcome.Failed, summary.Steps[0].Outcome);
        Assert.Contains("boom", summary.Steps[0].Messages);
        Assert.Equal(1, b.ExecuteCalls);
    }

    [Fact]
    public async Task Critical_step_exception_stops_run()
    {
        var h = new Harness();
        var a = Work("a"); a.IsCritical = true; a.Throws = new InvalidOperationException("boom");
        var b = Work("b");

        var summary = await h.Engine.RunAsync(Harness.Request(false, a, b), TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.StoppedOnCriticalFailure, summary.Status);
        Assert.Equal(0, b.ExecuteCalls);
        Assert.Contains("boom", summary.Steps[0].Messages);
    }

    [Fact]
    public async Task Reboot_registers_task_and_reboots()
    {
        var h = new Harness();
        var a = Work("a"); a.ExecuteResult = StepResult.Reboot("restart");
        var b = Work("b");

        var summary = await h.Engine.RunAsync(Harness.Request(false, a, b), TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.RebootPending, summary.Status);
        Assert.Equal(1, h.Tasks.RegisterCalls);
        Assert.Equal(0, h.Tasks.RemoveCalls);
        Assert.Equal([TimeSpan.FromSeconds(60)], h.Rebooter.Calls);
        Assert.Equal(0, b.ExecuteCalls);
        Assert.Empty(h.Reports.Written);
        var loaded = h.Store.Load();
        Assert.Equal(StateLoadStatus.Loaded, loaded.Status);
        Assert.Equal(StepOutcome.RebootRequired, loaded.State!.Results["a"].Outcome);
    }

    [Fact]
    public async Task Resume_continues_after_last_recorded_step()
    {
        var h = new Harness();
        h.Store.Save(new RunState("profile.json", "hash1", ["a", "b"],
            new Dictionary<string, StepRecord>
            {
                ["a"] = new(StepOutcome.Succeeded, DateTimeOffset.UtcNow, ["done"], null),
                ["gone"] = new(StepOutcome.Succeeded, DateTimeOffset.UtcNow, [], null),
            },
            DateTimeOffset.UtcNow));
        var a = Work("a"); var b = Work("b");

        var summary = await h.Engine.RunAsync(Harness.Request(true, a, b), TestContext.Current.CancellationToken);

        Assert.Equal(0, a.ExecuteCalls);
        Assert.Equal(1, b.ExecuteCalls);
        Assert.Equal(RunStatus.Completed, summary.Status);
        Assert.Equal(["a", "b"], summary.Steps.Select(s => s.Id));
        Assert.Contains("done", summary.Steps[0].Messages);
    }

    [Fact]
    public async Task Resume_without_state_returns_NothingToResume()
    {
        var h = new Harness();
        var a = Work("a");

        var summary = await h.Engine.RunAsync(Harness.Request(true, a), TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.NothingToResume, summary.Status);
        Assert.Equal(0, a.ExecuteCalls);
        Assert.Empty(h.Reports.Written);
    }

    [Fact]
    public async Task Resume_with_corrupt_state_returns_StateCorrupt()
    {
        var h = new Harness();
        h.Fs.Seed(Paths.StateFile, "{ not json");
        var a = Work("a");

        var summary = await h.Engine.RunAsync(Harness.Request(true, a), TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.StateCorrupt, summary.Status);
        Assert.False(string.IsNullOrEmpty(summary.Error));
        Assert.Equal(0, a.ExecuteCalls);
        Assert.True(h.Fs.FileExists(Paths.StateFile));
    }

    [Fact]
    public async Task Resume_with_changed_profile_returns_ProfileChanged()
    {
        var h = new Harness();
        h.Store.Save(new RunState("profile.json", "other-hash", ["a"],
            new Dictionary<string, StepRecord>(), DateTimeOffset.UtcNow));
        var a = Work("a");

        var summary = await h.Engine.RunAsync(Harness.Request(true, a), TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.ProfileChanged, summary.Status);
        Assert.Equal(0, a.ExecuteCalls);
        Assert.Empty(h.Reports.Written);
        Assert.Equal(StateLoadStatus.Loaded, h.Store.Load().Status);
    }

    [Fact]
    public async Task Cancellation_leaves_interrupted_step_unrecorded()
    {
        var h = new Harness();
        var a = Work("a");
        var b = Work("b"); b.Throws = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => h.Engine.RunAsync(Harness.Request(false, a, b), TestContext.Current.CancellationToken));

        var state = h.Store.Load().State!;
        Assert.Equal(["a"], state.Results.Keys);
        Assert.Empty(h.Reports.Written);
        Assert.Equal(0, h.Tasks.RemoveCalls);
    }

    [Fact]
    public async Task State_is_saved_after_each_step()
    {
        var h = new Harness();

        await h.Engine.RunAsync(Harness.Request(false, Work("a"), Work("b"), Work("c")), TestContext.Current.CancellationToken);

        Assert.True(h.Fs.WriteCount >= 4);
    }
}
