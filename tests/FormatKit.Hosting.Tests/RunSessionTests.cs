using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Hosting.Tests;

public sealed class RunSessionTests
{
    private sealed class RecordingProgress : IRunProgress
    {
        public List<string> Events { get; } = [];
        public void StepStarted(IStep step, int number, int total) => Events.Add($"start {step.Id} {number}/{total}");
        public void StepFinished(IStep step, StepResult result) => Events.Add($"end {step.Id} {result.Outcome}");
    }

    private sealed class ListSink : Serilog.Core.ILogEventSink
    {
        public List<Serilog.Events.LogEvent> Events { get; } = [];
        public void Emit(Serilog.Events.LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class Harness
    {
        public ListSink Sink { get; } = new();
        public InMemoryFileSystem Files { get; } = new();
        public FakeScheduledTasks Tasks { get; } = new();
        public FakeRebooter Rebooter { get; } = new();
        public FakeReportWriter Reports { get; } = new();
        public RunSession Session { get; }

        public Harness()
        {
            var env = new HostEnvironment(
                TestServices.Create(files: Files),
                new Elevation(),
                Tasks,
                Rebooter,
                Reports,
                new RunStateStore(Files, new AppPaths(@"C:\FormatKitTest")),
                new Serilog.LoggerConfiguration().WriteTo.Sink(Sink).CreateLogger(),
                TimeSpan.Zero,
                TimeSpan.Zero);
            Session = new RunSession(env);
        }
    }

    private static readonly LoadedProfile Profile = new(new Profile(1, "p", new Dictionary<string, System.Text.Json.JsonElement>()), "profile.json", "hash1");

    [Fact]
    public async Task Run_ReportsEachStepToProgress()
    {
        var h = new Harness();
        var progress = new RecordingProgress();
        IStep[] plan = [new FakeStep("a") { CheckResult = false }, new FakeStep("b") { CheckResult = true }];

        var summary = await h.Session.RunAsync(Profile, plan, false, progress, TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.Completed, summary.Status);
        Assert.Equal(["start a 1/2", "end a Succeeded", "start b 2/2", "end b Skipped"], progress.Events);
    }

    [Fact]
    public async Task Resume_NothingToResume_RemovesTask()
    {
        var h = new Harness();

        var summary = await h.Session.RunAsync(Profile, [new FakeStep("a")], true, new RecordingProgress(), TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.NothingToResume, summary.Status);
        Assert.Equal(1, h.Tasks.RemoveCalls);
        Assert.True(h.Session.ResumeTaskRemoved);
    }

    [Fact]
    public async Task Resume_Completed_DoesNotRemoveTaskItself()
    {
        var h = new Harness();
        var store = new RunStateStore(h.Files, new AppPaths(@"C:\FormatKitTest"));
        store.Save(new RunState("profile.json", "hash1", ["a"], new Dictionary<string, StepRecord>(), DateTimeOffset.UtcNow));

        var summary = await h.Session.RunAsync(
            Profile, [new FakeStep("a") { CheckResult = true }], true, new RecordingProgress(), TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.Completed, summary.Status);
        // The engine removes the task on completion; the session adds no second removal.
        Assert.Equal(1, h.Tasks.RemoveCalls);
    }

    [Fact]
    public async Task RemoveResumeTask_Throws_ReturnsFalseAndLogs()
    {
        var h = new Harness();
        h.Tasks.RemoveException = new InvalidOperationException("boom");

        Assert.False(await h.Session.RemoveResumeTaskAsync(TestContext.Current.CancellationToken));
        Assert.Contains(h.Sink.Events, e => e.Level == Serilog.Events.LogEventLevel.Warning && e.Exception is InvalidOperationException);
    }
}
