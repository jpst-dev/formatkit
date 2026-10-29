using System.Globalization;
using System.Resources;
using FormatKit.App.Localization;
using FormatKit.App.Tests.Fakes;
using FormatKit.App.Worker;
using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Hosting;
using Serilog;

namespace FormatKit.App.Tests;

public sealed class WorkerHostTests : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly string _dir = Directory.CreateTempSubdirectory("fk-worker-").FullName;
    private readonly FakeModule _module = new("fake");
    private readonly InMemoryFileSystem _files = new();
    private readonly FakeScheduledTasks _tasks = new();
    private readonly FakeReportWriter _reports = new();
    private readonly FakeProcessRunner _processes = new();
    private readonly DuplexStream _workerEnd;
    private readonly MessageChannel _worker;
    private readonly MessageChannel _window;
    private readonly WorkerHost _host;

    public WorkerHostTests()
    {
        (_workerEnd, var windowEnd) = DuplexStream.CreatePair();
        var log = new LoggerConfiguration().CreateLogger();
        _worker = new MessageChannel(_workerEnd, log);
        _window = new MessageChannel(windowEnd, log);
        var rebooter = new PipeRebooter(_worker, _processes, log);
        var env = new HostEnvironment(
            TestServices.Create(files: _files),
            new Elevation(),
            _tasks,
            rebooter,
            _reports,
            Store,
            log,
            TimeSpan.Zero,
            TimeSpan.Zero);
        _host = new WorkerHost(env, [_module], rebooter);
    }

    private RunStateStore Store => new(_files, new AppPaths(@"C:\FormatKitTest"));

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _window.DisposeAsync();
        Directory.Delete(_dir, recursive: true);
    }

    private string WriteProfile()
    {
        var path = Path.Combine(_dir, "profile.json");
        File.WriteAllText(path, """{"schemaVersion":1,"name":"Mine","modules":{"fake":{"enabled":true}}}""");
        return path;
    }

    private string HashOf(string path) => new ProfileService([_module]).Load(path).Profile!.Hash;

    private string[] RunJob(string path) => ["run", "--profile", path, "--profile-hash", HashOf(path)];

    /// <summary>Ends the worker's side of the connection and returns everything the window received.</summary>
    private async Task<List<WorkerMessage>> WindowMessages()
    {
        await _worker.DisposeAsync();
        var list = new List<WorkerMessage>();
        await foreach (var m in _window.ReadAllAsync(Ct)) list.Add(m);
        return list;
    }

    [Fact]
    public async Task Run_SendsPlannedStepsAndFinished()
    {
        _module.Steps.Add(new FakeStep("a") { Title = "Step A", RequiresAdmin = true, CheckResult = false });
        _module.Steps.Add(new FakeStep("b") { Title = "Step B", CheckResult = true });
        var path = WriteProfile();

        var exit = await _host.RunAsync(_worker, RunJob(path), Ct);

        Assert.Equal(0, exit);
        var messages = await WindowMessages();
        Assert.Equal(6, messages.Count);
        var planned = Assert.IsType<Planned>(messages[0]);
        Assert.Equal([new PlannedStep("a", "Step A", true), new PlannedStep("b", "Step B", false)], planned.Steps);
        Assert.Equal(new StepStarted(1, 2, "a"), messages[1]);
        var a = Assert.IsType<StepFinished>(messages[2]);
        Assert.Equal(("a", StepOutcome.Succeeded), (a.Id, a.Outcome));
        Assert.Equal(new StepStarted(2, 2, "b"), messages[3]);
        var b = Assert.IsType<StepFinished>(messages[4]);
        Assert.Equal(("b", StepOutcome.Skipped), (b.Id, b.Outcome));
        var finished = Assert.IsType<Finished>(messages[5]);
        Assert.Equal(RunStatus.Completed, finished.Summary.Status);
        Assert.Equal(FakeReportWriter.Directory, finished.Summary.ReportDirectory);
    }

    [Fact]
    public async Task Run_HashMismatch_SendsFailedAndRunsNothing()
    {
        var step = new FakeStep("a") { CheckResult = false };
        _module.Steps.Add(step);
        var path = WriteProfile();

        var exit = await _host.RunAsync(_worker, ["run", "--profile", path, "--profile-hash", "not-the-hash"], Ct);

        Assert.Equal(2, exit);
        var failed = Assert.IsType<Failed>(Assert.Single(await WindowMessages()));
        Assert.Equal(Strings.Get("Worker_ProfileChanged"), failed.Message);
        var manager = new ResourceManager("FormatKit.App.Resources.Strings", typeof(Strings).Assembly);
        Assert.Equal(
            "The profile changed since you reviewed it. Review it again.",
            manager.GetString("Worker_ProfileChanged", CultureInfo.InvariantCulture));
        Assert.Equal(0, step.CheckCalls);
        Assert.Equal(StateLoadStatus.None, Store.Load().Status);
    }

    [Fact]
    public async Task Run_ProfileDeleted_SendsFailedWithErrors()
    {
        var path = WriteProfile();
        var job = RunJob(path);
        File.Delete(path);
        var errors = new ProfileService([_module]).Load(path).Errors;

        var exit = await _host.RunAsync(_worker, job, Ct);

        Assert.Equal(2, exit);
        var failed = Assert.IsType<Failed>(Assert.Single(await WindowMessages()));
        Assert.StartsWith(Strings.Format("Worker_ProfileErrors", errors.Count), failed.Message);
        Assert.All(errors, e => Assert.Contains(e.Message, failed.Message));
    }

    [Fact]
    public async Task Cancel_StopsAfterCurrentStep()
    {
        var blocking = new BlockingStep("a");
        var next = new FakeStep("b") { CheckResult = false };
        _module.Steps.AddRange([blocking, next]);
        var path = WriteProfile();

        var run = _host.RunAsync(_worker, RunJob(path), Ct);
        await blocking.Started.WaitAsync(Wait, Ct);
        await _window.SendAsync(new Cancel(), Ct);
        await _host.StopRequested.WaitAsync(Wait, Ct); // the read loop took the cancel while the step still runs
        blocking.Release();
        var exit = await run.WaitAsync(Wait, Ct);

        Assert.Equal(2, exit);
        Assert.False(blocking.SawCancellation);
        Assert.Equal(0, next.CheckCalls);
        var messages = await WindowMessages();
        Assert.DoesNotContain(messages, m => m is StepStarted { Id: "b" });
        Assert.Contains(messages, m => m is StepFinished { Id: "a", Outcome: StepOutcome.Succeeded });
        Assert.Equal(Strings.Get("Worker_Stopped"), Assert.IsType<Failed>(messages[^1]).Message);
        // The run stays saved for Resume.
        var state = Store.Load();
        Assert.Equal(StateLoadStatus.Loaded, state.Status);
        Assert.Equal(["a"], state.State!.Results.Keys);
    }

    [Fact]
    public async Task StreamClosed_CancelsRun()
    {
        var blocking = new BlockingStep("a");
        var next = new FakeStep("b") { CheckResult = false };
        _module.Steps.AddRange([blocking, next]);
        var path = WriteProfile();

        var run = _host.RunAsync(_worker, RunJob(path), Ct);
        await blocking.Started.WaitAsync(Wait, Ct);
        await _window.DisposeAsync();
        await _host.StopRequested.WaitAsync(Wait, Ct);
        blocking.Release();
        var exit = await run.WaitAsync(Wait, Ct);

        Assert.Equal(2, exit);
        Assert.Equal(0, next.CheckCalls);
        Assert.False(blocking.SawCancellation);
        Assert.Equal(StateLoadStatus.Loaded, Store.Load().Status);
    }

    [Fact]
    public async Task Resume_NothingToResume_SendsFinishedWithStatus()
    {
        var exit = await _host.RunAsync(_worker, ["run", "--resume"], Ct);

        Assert.Equal(0, exit);
        var finished = Assert.IsType<Finished>(Assert.Single(await WindowMessages()));
        Assert.Equal(RunStatus.NothingToResume, finished.Summary.Status);
        Assert.Equal(1, _tasks.RemoveCalls);
    }

    [Fact]
    public async Task Resume_ProfileInvalid_SendsNothingToResumeAndRemovesTask()
    {
        Store.Save(new RunState(Path.Combine(_dir, "gone.json"), "h", ["a"], new Dictionary<string, StepRecord>(), DateTimeOffset.UtcNow));

        var exit = await _host.RunAsync(_worker, ["run", "--resume"], Ct);

        Assert.Equal(0, exit);
        var finished = Assert.IsType<Finished>(Assert.Single(await WindowMessages()));
        Assert.Equal(RunStatus.NothingToResume, finished.Summary.Status);
        Assert.Equal(1, _tasks.RemoveCalls);
        var errors = new ProfileService([_module]).Load(Path.Combine(_dir, "gone.json")).Errors;
        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.Contains(e.Message, finished.Summary.Error));
    }

    [Fact]
    public async Task Resume_CorruptState_SendsStateCorruptAndRemovesTask()
    {
        _files.Seed(new AppPaths(@"C:\FormatKitTest").StateFile, "{ not json");

        var exit = await _host.RunAsync(_worker, ["run", "--resume"], Ct);

        Assert.Equal(0, exit);
        var finished = Assert.IsType<Finished>(Assert.Single(await WindowMessages()));
        Assert.Equal(RunStatus.StateCorrupt, finished.Summary.Status);
        Assert.Equal(1, _tasks.RemoveCalls);
        var corrupt = Store.Load().Error;
        Assert.False(string.IsNullOrEmpty(corrupt));
        Assert.Equal(corrupt, finished.Summary.Error);
    }

    [Fact]
    public async Task Resume_SavedRun_RunsTheRemainingSteps()
    {
        var a = new FakeStep("a") { CheckResult = false };
        var b = new FakeStep("b") { CheckResult = false };
        _module.Steps.AddRange([a, b]);
        var path = WriteProfile();
        Store.Save(new RunState(path, HashOf(path), ["a", "b"],
            new Dictionary<string, StepRecord> { ["a"] = new(StepOutcome.Succeeded, DateTimeOffset.UtcNow, [], null) },
            DateTimeOffset.UtcNow));

        var exit = await _host.RunAsync(_worker, ["run", "--resume"], Ct);

        Assert.Equal(0, exit);
        Assert.Equal(0, a.ExecuteCalls);
        Assert.Equal(1, b.ExecuteCalls);
        var messages = await WindowMessages();
        Assert.Contains(messages, m => m is StepStarted { Id: "b", Number: 2, Total: 2 });
        Assert.Equal(RunStatus.Completed, Assert.IsType<Finished>(messages[^1]).Summary.Status);
    }

    [Fact]
    public async Task Resume_PlannedMarksTheStepsDoneBefore()
    {
        var a = new FakeStep("a") { CheckResult = false };
        var b = new FakeStep("b") { CheckResult = false };
        var c = new FakeStep("c") { CheckResult = false };
        _module.Steps.AddRange([a, b, c]);
        var path = WriteProfile();
        Store.Save(new RunState(path, HashOf(path), ["a", "b", "c"],
            new Dictionary<string, StepRecord>
            {
                ["a"] = new(StepOutcome.Succeeded, DateTimeOffset.UtcNow, [], null),
                ["b"] = new(StepOutcome.Skipped, DateTimeOffset.UtcNow, [], null),
            },
            DateTimeOffset.UtcNow));

        await _host.RunAsync(_worker, ["run", "--resume"], Ct);

        var planned = Assert.IsType<Planned>((await WindowMessages())[0]);
        Assert.Equal(
            [StepOutcome.Succeeded, StepOutcome.Skipped, null],
            planned.Steps.Select(s => s.DoneBefore));
    }

    [Fact]
    public async Task Run_StepThrowsTaskCanceled_SendsFailedWithError()
    {
        _module.Steps.Add(new FakeStep("a") { CheckResult = false, Throws = new TaskCanceledException("request timed out") });
        var path = WriteProfile();

        var exit = await _host.RunAsync(_worker, RunJob(path), Ct).WaitAsync(Wait, Ct);

        Assert.Equal(2, exit);
        var failed = Assert.IsType<Failed>((await WindowMessages())[^1]);
        Assert.Equal(Strings.Format("Worker_Error", "request timed out"), failed.Message);
    }

    [Fact]
    public async Task Reboot_CountdownComesAfterTheRebootStepFinished()
    {
        _module.Steps.Add(new FakeStep("a") { CheckResult = false, ExecuteResult = StepResult.Reboot("restart needed") });
        var path = WriteProfile();
        _workerEnd.WriteDelay = TimeSpan.FromMilliseconds(20);
        _processes.Enqueue("shutdown.exe", new ProcessResult(0, "", "", TimeSpan.Zero, false)); // the countdown is zero

        var exit = await _host.RunAsync(_worker, RunJob(path), Ct).WaitAsync(Wait, Ct);
        var received = await WindowMessages();

        Assert.Equal(0, exit);
        var finishedAt = received.FindIndex(m => m is StepFinished { Id: "a", Outcome: StepOutcome.RebootRequired });
        var countdownAt = received.FindIndex(m => m is RebootCountdown);
        Assert.True(finishedAt >= 0 && countdownAt > finishedAt, string.Join(", ", received.Select(m => m.GetType().Name)));
        Assert.Equal(RunStatus.RebootPending, Assert.IsType<Finished>(received[^1]).Summary.Status);
    }

    [Fact]
    public async Task ReadLoopError_StopsAfterCurrentStepAndStillReturns()
    {
        var blocking = new BlockingStep("a");
        var next = new FakeStep("b") { CheckResult = false };
        _module.Steps.AddRange([blocking, next]);
        var path = WriteProfile();

        var run = _host.RunAsync(_worker, RunJob(path), Ct);
        await blocking.Started.WaitAsync(Wait, Ct);
        _workerEnd.ReadException = new InvalidOperationException("reader broke");
        await _window.SendAsync(new RebootNow(), Ct); // wake the pending read so the next one throws
        await _host.StopRequested.WaitAsync(Wait, Ct);
        blocking.Release();
        var exit = await run.WaitAsync(Wait, Ct);

        Assert.Equal(2, exit);
        Assert.Equal(0, next.CheckCalls);
        Assert.Equal(Strings.Get("Worker_Stopped"), Assert.IsType<Failed>((await WindowMessages())[^1]).Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("run")]
    [InlineData("run --profile x.json")]
    [InlineData("run --profile-hash h")]
    [InlineData("query --resume")]
    [InlineData("run --resume --profile x.json")]
    public async Task Run_InvalidJob_SendsFailed(string job)
    {
        var exit = await _host.RunAsync(_worker, job.Split(' ', StringSplitOptions.RemoveEmptyEntries), Ct);

        Assert.Equal(2, exit);
        Assert.IsType<Failed>(Assert.Single(await WindowMessages()));
    }

    /// <summary>A step that blocks in Execute until released, ignoring its token like a step mid-work.</summary>
    private sealed class BlockingStep(string id) : IStep
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id { get; } = id;
        public string Title => Id;
        public bool RequiresAdmin => false;
        public bool IsCritical => false;
        public Task Started => _started.Task;
        public bool SawCancellation { get; private set; }

        public void Release() => _release.TrySetResult();

        public Task<bool> CheckAsync(StepContext ctx, CancellationToken ct) => Task.FromResult(false);

        public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
        {
            _started.TrySetResult();
            await _release.Task;
            SawCancellation = ct.IsCancellationRequested;
            return StepResult.Success();
        }
    }
}
