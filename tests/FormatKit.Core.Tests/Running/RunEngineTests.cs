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
        public RecordingProgress Progress { get; } = new();
        public RunEngine Engine { get; }
        public DateTimeOffset LastBoot { get; set; } = DateTimeOffset.MinValue;

        public Harness()
        {
            Store = new RunStateStore(Fs, Paths);
            Engine = new RunEngine(Store, Tasks, Rebooter, Reports, TimeSpan.FromSeconds(60), progress: Progress, lastBootTime: () => LastBoot);
        }

        public static RunRequest Request(bool resume, params IStep[] plan) =>
            new("profile.json", "hash1", plan,
                new StepContext(null!, new Serilog.LoggerConfiguration().CreateLogger()), resume);
    }

    private static FakeStep Work(string id) => new(id) { CheckResult = false };

    private sealed class RecordingProgress : IRunProgress
    {
        public List<string> Events { get; } = [];
        public void StepStarted(IStep step, int number, int total) => Events.Add($"start {step.Id} {number}/{total}");
        public void StepFinished(IStep step, StepResult result) => Events.Add($"end {step.Id} {result.Outcome}");
    }

    [Fact]
    public async Task Reports_progress_for_each_step_it_runs()
    {
        var h = new Harness();
        var a = Work("a"); var b = new FakeStep("b") { CheckResult = true };

        await h.Engine.RunAsync(Harness.Request(false, a, b), TestContext.Current.CancellationToken);

        Assert.Equal(["start a 1/2", "end a Succeeded", "start b 2/2", "end b Skipped"], h.Progress.Events);
    }

    [Fact]
    public async Task Resume_reports_progress_only_for_unfinished_steps_with_plan_numbers()
    {
        var h = new Harness();
        h.Store.Save(new RunState("profile.json", "hash1", ["a", "b"],
            new Dictionary<string, StepRecord> { ["a"] = new(StepOutcome.Succeeded, DateTimeOffset.UtcNow, [], null) },
            DateTimeOffset.UtcNow));

        await h.Engine.RunAsync(Harness.Request(true, Work("a"), Work("b")), TestContext.Current.CancellationToken);

        Assert.Equal(["start b 2/2", "end b Succeeded"], h.Progress.Events);
    }

    [Fact]
    public async Task Step_skipped_by_its_check_says_why()
    {
        var h = new Harness();
        var a = new FakeStep("a") { CheckResult = true };

        var summary = await h.Engine.RunAsync(Harness.Request(false, a), TestContext.Current.CancellationToken);

        var step = Assert.Single(summary.Steps);
        Assert.Equal(StepOutcome.Skipped, step.Outcome);
        Assert.Single(step.Messages);
        Assert.False(string.IsNullOrWhiteSpace(step.Messages[0]));
    }

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
    public async Task Resume_after_a_reboot_marks_the_reboot_step_as_succeeded()
    {
        var h = new Harness();
        var finished = DateTimeOffset.UtcNow.AddMinutes(-5);
        h.Store.Save(new RunState("profile.json", "hash1", ["a", "b"],
            new Dictionary<string, StepRecord> { ["a"] = new(StepOutcome.RebootRequired, finished, ["restart"], null) },
            finished));
        h.LastBoot = finished.AddMinutes(1);

        var summary = await h.Engine.RunAsync(Harness.Request(true, Work("a"), Work("b")), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, summary.Steps[0].Outcome);
        Assert.Equal(2, summary.Steps[0].Messages.Count);
        Assert.Equal("restart", summary.Steps[0].Messages[0]);
        Assert.Equal(RunStatus.Completed, summary.Status);
    }

    [Fact]
    public async Task Step_that_asks_to_run_again_runs_again_after_the_reboot()
    {
        var h = new Harness();
        var a = new FakeStep("a") { CheckResult = false, ExecuteResult = StepResult.RebootAndRunAgain("platform") };

        await h.Engine.RunAsync(Harness.Request(false, a, Work("b")), TestContext.Current.CancellationToken);
        Assert.True(h.Store.Load().State!.Results["a"].RunAgain);

        h.LastBoot = DateTimeOffset.UtcNow.AddSeconds(1);
        a.ExecuteResult = StepResult.Success("distro");
        var summary = await h.Engine.RunAsync(Harness.Request(true, a, Work("b")), TestContext.Current.CancellationToken);

        Assert.Equal(2, a.ExecuteCalls);
        Assert.Equal(StepOutcome.Succeeded, summary.Steps[0].Outcome);
        Assert.Equal(["distro"], summary.Steps[0].Messages);
        Assert.Equal(RunStatus.Completed, summary.Status);
    }

    [Fact]
    public async Task Step_runs_again_only_once_so_reboots_cannot_loop()
    {
        var h = new Harness();
        var a = new FakeStep("a") { CheckResult = false, ExecuteResult = StepResult.RebootAndRunAgain("platform") };

        await h.Engine.RunAsync(Harness.Request(false, a), TestContext.Current.CancellationToken);
        h.LastBoot = DateTimeOffset.UtcNow.AddSeconds(1);
        await h.Engine.RunAsync(Harness.Request(true, a), TestContext.Current.CancellationToken);
        h.LastBoot = DateTimeOffset.UtcNow.AddSeconds(2);
        var summary = await h.Engine.RunAsync(Harness.Request(true, a), TestContext.Current.CancellationToken);

        Assert.Equal(2, a.ExecuteCalls);
        Assert.Equal(StepOutcome.Succeeded, summary.Steps[0].Outcome);
        Assert.Equal(RunStatus.Completed, summary.Status);
    }

    [Fact]
    public async Task Resume_without_a_reboot_keeps_the_step_as_reboot_required()
    {
        var h = new Harness();
        var finished = DateTimeOffset.UtcNow.AddMinutes(-5);
        h.Store.Save(new RunState("profile.json", "hash1", ["a", "b"],
            new Dictionary<string, StepRecord> { ["a"] = new(StepOutcome.RebootRequired, finished, ["restart"], null) },
            finished));
        h.LastBoot = finished.AddMinutes(-60);

        var summary = await h.Engine.RunAsync(Harness.Request(true, Work("a"), Work("b")), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.RebootRequired, summary.Steps[0].Outcome);
        Assert.Equal(["restart"], summary.Steps[0].Messages);
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

    [Fact]
    public async Task Task_removal_failure_does_not_prevent_report_or_state_cleanup()
    {
        var h = new Harness();
        h.Tasks.RemoveException = new InvalidOperationException("schtasks broke");
        var a = Work("a");

        var summary = await h.Engine.RunAsync(Harness.Request(false, a), TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.Completed, summary.Status);
        Assert.Equal(1, h.Tasks.RemoveCalls);
        Assert.Single(h.Reports.Written);
        Assert.Equal(FakeReportWriter.Directory, summary.ReportDirectory);
        Assert.Equal(StateLoadStatus.None, h.Store.Load().Status);
    }

    [Fact]
    public async Task Reboot_task_registration_failure_still_reboots_and_flags_summary()
    {
        var h = new Harness();
        h.Tasks.RegisterException = new InvalidOperationException("access denied");
        var a = Work("a"); a.ExecuteResult = StepResult.Reboot("restart");

        var summary = await h.Engine.RunAsync(Harness.Request(false, a), TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.RebootPending, summary.Status);
        Assert.True(summary.ResumeScheduleFailed);
        Assert.Single(h.Rebooter.Calls);
        Assert.Equal(StateLoadStatus.Loaded, h.Store.Load().Status);
    }

    [Fact]
    public async Task Reboot_with_successful_registration_does_not_flag_summary()
    {
        var h = new Harness();
        var a = Work("a"); a.ExecuteResult = StepResult.Reboot("restart");

        var summary = await h.Engine.RunAsync(Harness.Request(false, a), TestContext.Current.CancellationToken);

        Assert.False(summary.ResumeScheduleFailed);
    }
}
