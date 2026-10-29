using System.Xml.Linq;
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

public sealed class RunProfileRunTests : IDisposable
{
    private const string AdminJson =
        """{"schemaVersion":1,"name":"Mine","modules":{"drivers":{"enabled":true},"programs":{"enabled":true}}}""";

    private const string NoAdminJson =
        """{"schemaVersion":1,"name":"Mine","modules":{"programs":{"enabled":true}}}""";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _dir = Directory.CreateTempSubdirectory("fk-run-").FullName;
    private readonly InMemoryFileSystem _files = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeWorkerLauncher _launcher = new();
    private readonly FakeAppElevation _elevation = new();
    private readonly FakeModule _drivers = new("drivers");
    private readonly FakeModule _programs = new("programs");
    private readonly RunStateStore _store;

    public RunProfileRunTests()
    {
        _drivers.Steps.Add(new FakeStep("drivers.scan") { Title = "Scan drivers", RequiresAdmin = true });
        _programs.Steps.Add(new FakeStep("programs.winget") { Title = "Install programs" });
        _programs.Steps.Add(new FakeStep("programs.direct") { Title = "Direct installs" });
        _store = TestViewModels.Store(_files);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private RunProfileViewModel Create()
    {
        IReadOnlyList<IModule> modules = [_drivers, _programs];
        var env = new HostEnvironment(
            TestServices.Create(files: _files), _elevation, new FakeScheduledTasks(), new FakeRebooter(),
            new FakeReportWriter(), _store, new Serilog.LoggerConfiguration().CreateLogger(), TimeSpan.Zero, TimeSpan.Zero);
        return new RunProfileViewModel(
            new ProfileService(modules), modules, _launcher, _elevation, _dialogs,
            new AppSettings(_files, @"C:\FormatKitTest\app.json"), env);
    }

    /// <summary>A view model reviewing a profile file with <paramref name="json"/>.</summary>
    private RunProfileViewModel Reviewed(string json = AdminJson)
    {
        var path = Path.Combine(_dir, "profile.json");
        File.WriteAllText(path, json);
        _dialogs.OpenFileResult = path;
        var vm = Create();
        vm.OpenCommand.Execute(null);
        Assert.Equal(RunPhase.Review, vm.Phase);
        return vm;
    }

    private static Planned PlannedAll() => new([
        new PlannedStep("drivers.scan", "Scan drivers", true),
        new PlannedStep("programs.winget", "Install programs", false),
        new PlannedStep("programs.direct", "Direct installs", false),
    ]);

    private static Finished FinishedWith(RunStatus status, bool resumeScheduleFailed = false) =>
        new(new RunSummary(status, [], @"C:\reports\run-1", null, resumeScheduleFailed));

    private void SeedState() =>
        _store.Save(new RunState("p.json", "hash", ["drivers.scan"], [], DateTimeOffset.UnixEpoch));

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not reached");
            await Task.Delay(10, Ct);
        }
    }

    [Fact]
    public async Task Run_ConfirmDeclined_StartsNothing()
    {
        var vm = Reviewed();
        _dialogs.ConfirmResult = false;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Confirmations);
        Assert.Empty(_launcher.Jobs);
        Assert.Equal(RunPhase.Review, vm.Phase);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task Run_UacDeclined_BackToReviewWithMessage()
    {
        var vm = Reviewed();
        _launcher.Declines = true;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal([["run", "--profile", vm.ProfilePath!, "--profile-hash", vm.ReviewedHash!]], _launcher.Jobs);
        Assert.Equal([true], _launcher.Elevations);
        Assert.Equal(RunPhase.Review, vm.Phase);
        Assert.Equal(Strings.Get("RunProfile_UacDeclined"), vm.RunMessage);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task Run_NoAdminSteps_StartsWithoutElevation()
    {
        var vm = Reviewed(NoAdminJson);
        _launcher.Script.Add(FinishedWith(RunStatus.Completed));

        await vm.RunCommand.ExecuteAsync(null).WaitAsync(Timeout, Ct);

        Assert.Equal([false], _launcher.Elevations);
    }

    [Fact]
    public async Task Run_AdminStepsWindowElevated_StartsWithoutElevation()
    {
        _elevation.IsElevated = true;
        var vm = Reviewed();
        _launcher.Script.Add(FinishedWith(RunStatus.Completed));

        await vm.RunCommand.ExecuteAsync(null).WaitAsync(Timeout, Ct);

        Assert.Equal([false], _launcher.Elevations);
    }

    [Fact]
    public async Task Progress_UpdatesStepStates()
    {
        var vm = Reviewed();
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _launcher.Script.AddRange([
            PlannedAll(),
            new StepStarted(1, 3, "drivers.scan"),
            new StepFinished("drivers.scan", StepOutcome.Succeeded, ["3 drivers installed"], null),
            new StepStarted(2, 3, "programs.winget"),
            new Do(() => secondStarted.Task),
            new StepFinished("programs.winget", StepOutcome.Failed, ["winget failed"], "Install it by hand"),
            new StepStarted(3, 3, "programs.direct"),
            new StepFinished("programs.direct", StepOutcome.Skipped, [], null),
            FinishedWith(RunStatus.CompletedWithFailures),
        ]);

        var run = vm.RunCommand.ExecuteAsync(null);
        await WaitUntil(() => vm.Steps.Count == 3 && vm.Steps[1].State == StepState.Running);

        Assert.True(vm.IsRunning);
        Assert.Equal(RunPhase.Running, vm.Phase);
        Assert.Equal(["drivers.scan", "programs.winget", "programs.direct"], vm.Steps.Select(s => s.Id));
        Assert.Equal([StepState.Succeeded, StepState.Running, StepState.Pending], vm.Steps.Select(s => s.State));
        Assert.True(vm.Steps[1].IsCurrent);

        secondStarted.SetResult();
        await run.WaitAsync(Timeout, Ct);

        Assert.Equal([StepState.Succeeded, StepState.Failed, StepState.Skipped], vm.Steps.Select(s => s.State));
        Assert.Equal(["3 drivers installed"], vm.Steps[0].Messages);
        Assert.Equal("Install it by hand", vm.Steps[1].NextAction);
        Assert.Equal(RunPhase.Result, vm.Phase);
        Assert.False(vm.IsRunning);
    }

    [Theory]
    [InlineData(RunStatus.Completed, false, "Run_Status_Completed")]
    [InlineData(RunStatus.CompletedWithFailures, false, "Run_Status_CompletedWithFailures")]
    [InlineData(RunStatus.StoppedOnCriticalFailure, false, "Run_Status_StoppedOnCriticalFailure")]
    [InlineData(RunStatus.RebootPending, false, "Run_RebootPending")]
    [InlineData(RunStatus.RebootPending, true, "Run_ResumeNotScheduled")]
    [InlineData(RunStatus.ProfileChanged, false, "Run_ProfileChanged")]
    [InlineData(RunStatus.NothingToResume, false, "Run_NothingToResume")]
    [InlineData(RunStatus.StateCorrupt, false, "Run_StateCorrupt")]
    public async Task Finished_ShowsCliStatusText(RunStatus status, bool resumeScheduleFailed, string key)
    {
        var vm = Reviewed();
        _launcher.Script.Add(FinishedWith(status, resumeScheduleFailed));

        await vm.RunCommand.ExecuteAsync(null).WaitAsync(Timeout, Ct);

        Assert.Equal(RunPhase.Result, vm.Phase);
        Assert.Equal(Strings.Format(key, ""), vm.FinalStatus);
        Assert.Equal(@"C:\reports\run-1", vm.ReportDirectory);
    }

    [Fact]
    public void CopiedCliTexts_MatchTheCliResources()
    {
        string[] keys =
        [
            "Run_Status_Completed", "Run_Status_CompletedWithFailures", "Run_Status_StoppedOnCriticalFailure",
            "Run_RebootPending", "Run_ResumeNotScheduled", "Run_ProfileChanged", "Run_NothingToResume",
            "Run_StateCorrupt", "Run_NextAction",
        ];
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "src", "FormatKit.Cli", "Resources", "Strings.resx")))
            root = root.Parent ?? throw new InvalidOperationException("repository root not found");

        foreach (var file in new[] { "Strings.resx", "Strings.pt-BR.resx" })
        {
            var cli = Values(Path.Combine(root.FullName, "src", "FormatKit.Cli", "Resources", file));
            var app = Values(Path.Combine(root.FullName, "src", "FormatKit.App", "Resources", file));
            foreach (var key in keys)
                Assert.Equal(cli[key], app.GetValueOrDefault(key));
        }

        static Dictionary<string, string> Values(string path) =>
            XDocument.Load(path).Root!.Elements("data")
                .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);
    }

    [Fact]
    public async Task Failed_ShowsMessage()
    {
        var vm = Reviewed();
        _launcher.Script.AddRange([PlannedAll(), new Failed("The profile changed since you reviewed it.")]);

        await vm.RunCommand.ExecuteAsync(null).WaitAsync(Timeout, Ct);

        Assert.Equal(RunPhase.Result, vm.Phase);
        Assert.Equal("The profile changed since you reviewed it.", vm.FinalStatus);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task Stop_SendsCancel()
    {
        var vm = Reviewed();
        _launcher.Script.AddRange([
            PlannedAll(),
            new StepStarted(1, 3, "drivers.scan"),
            new Expect<Cancel>(),
            new StepFinished("drivers.scan", StepOutcome.Succeeded, [], null),
            new Do(SeedState),
            new Failed(Strings.Get("Worker_Stopped")),
        ]);

        var run = vm.RunCommand.ExecuteAsync(null);
        await WaitUntil(() => vm.Steps.Count == 3 && vm.Steps[0].State == StepState.Running);
        Assert.True(vm.StopCommand.CanExecute(null));
        await vm.StopCommand.ExecuteAsync(null);
        await run.WaitAsync(Timeout, Ct);

        Assert.Single(_launcher.Received.OfType<Cancel>());
        Assert.Equal(Strings.Get("RunProfile_Stopped"), vm.FinalStatus);
        Assert.True(vm.HasInterruptedRun);
        Assert.Equal(StepState.Succeeded, vm.Steps[0].State);
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    [Fact]
    public async Task Reboot_Cancel_SendsRebootCancel()
    {
        var vm = Reviewed();
        _dialogs.RebootResult = false;
        _launcher.Script.AddRange([
            PlannedAll(),
            new StepStarted(1, 3, "drivers.scan"),
            new StepFinished("drivers.scan", StepOutcome.RebootRequired, [], null),
            new RebootCountdown(60),
            new Expect<RebootCancel>(),
            FinishedWith(RunStatus.RebootPending),
        ]);

        await vm.RunCommand.ExecuteAsync(null).WaitAsync(Timeout, Ct);

        Assert.Equal([60], _dialogs.RebootCountdowns);
        Assert.Single(_launcher.Received.OfType<RebootCancel>());
        Assert.Empty(_launcher.Received.OfType<RebootNow>());
        Assert.Equal(StepState.RebootRequired, vm.Steps[0].State);
        Assert.Equal(Strings.Get("Run_RebootPending"), vm.FinalStatus);
    }

    [Fact]
    public async Task Reboot_Accept_SendsRebootNow()
    {
        var vm = Reviewed();
        _dialogs.RebootResult = true;
        _launcher.Script.AddRange([PlannedAll(), new RebootCountdown(60), new Expect<RebootNow>()]);

        await vm.RunCommand.ExecuteAsync(null).WaitAsync(Timeout, Ct);

        Assert.Equal([60], _dialogs.RebootCountdowns);
        Assert.Single(_launcher.Received.OfType<RebootNow>());
        Assert.Empty(_launcher.Received.OfType<RebootCancel>());
    }

    [Fact]
    public async Task ChannelEndsEarly_ShowsUnexpectedEnd()
    {
        var vm = Reviewed();
        _launcher.Script.AddRange([PlannedAll(), new StepStarted(1, 3, "drivers.scan"), new Do(SeedState)]);
        Assert.False(vm.HasInterruptedRun);

        await vm.RunCommand.ExecuteAsync(null).WaitAsync(Timeout, Ct);

        Assert.Equal(RunPhase.Result, vm.Phase);
        Assert.Equal(Strings.Get("RunProfile_UnexpectedEnd"), vm.FinalStatus);
        Assert.True(vm.HasInterruptedRun);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task ConfirmClose_NotRunning_Closes()
    {
        var vm = Reviewed();

        Assert.True(await vm.ConfirmCloseAsync());
        Assert.Empty(_dialogs.Confirmations);
    }

    [Fact]
    public async Task ConfirmClose_RunningAndYes_SendsCancelAndWaits()
    {
        var vm = Reviewed();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _launcher.Script.AddRange([
            PlannedAll(),
            new StepStarted(1, 3, "drivers.scan"),
            new Expect<Cancel>(),
            new Do(() => release.Task),
            new Failed(Strings.Get("Worker_Stopped")),
        ]);
        var run = vm.RunCommand.ExecuteAsync(null);
        await WaitUntil(() => vm.Steps.Count == 3 && vm.Steps[0].State == StepState.Running);
        _dialogs.Confirmations.Clear();

        var close = vm.ConfirmCloseAsync();
        await _launcher.WaitForAsync<Cancel>().WaitAsync(Timeout, Ct);
        await Task.Delay(50, Ct);
        Assert.False(close.IsCompleted);

        release.SetResult();
        Assert.True(await close.WaitAsync(Timeout, Ct));
        await run.WaitAsync(Timeout, Ct);

        Assert.Equal(Strings.Get("RunProfile_StopQuestion"), Assert.Single(_dialogs.Confirmations).Message);
        Assert.Single(_launcher.Received.OfType<Cancel>());
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task ConfirmClose_RunningAndNo_StaysOpen()
    {
        var vm = Reviewed();
        _launcher.Script.AddRange([PlannedAll(), new StepStarted(1, 3, "drivers.scan"), new Expect<Cancel>()]);
        var run = vm.RunCommand.ExecuteAsync(null);
        await WaitUntil(() => vm.Steps.Count == 3 && vm.Steps[0].State == StepState.Running);
        _dialogs.ConfirmResult = false;

        Assert.False(await vm.ConfirmCloseAsync().WaitAsync(Timeout, Ct));
        await Task.Delay(50, Ct);

        Assert.Empty(_launcher.Received.OfType<Cancel>());
        Assert.True(vm.IsRunning);

        await vm.StopCommand.ExecuteAsync(null);
        await run.WaitAsync(Timeout, Ct);
    }

    [Fact]
    public async Task OpenReport_OpensTheReportDirectory()
    {
        var vm = Reviewed();
        Assert.False(vm.OpenReportCommand.CanExecute(null));
        _launcher.Script.Add(FinishedWith(RunStatus.Completed));

        await vm.RunCommand.ExecuteAsync(null).WaitAsync(Timeout, Ct);
        vm.OpenReportCommand.Execute(null);

        Assert.Equal([@"C:\reports\run-1"], _dialogs.OpenedFolders);
    }

    [Fact]
    public void Review_NoSteps_ShowsNoStepsAndCannotRun()
    {
        var vm = Reviewed("""{"schemaVersion":1,"name":"Mine","modules":{}}""");

        Assert.True(vm.HasNoSteps);
        Assert.False(vm.RunCommand.CanExecute(null));
    }

    [Fact]
    public void Review_WithSteps_CanRun()
    {
        var vm = Reviewed();

        Assert.False(vm.HasNoSteps);
        Assert.True(vm.RunCommand.CanExecute(null));
    }
}
