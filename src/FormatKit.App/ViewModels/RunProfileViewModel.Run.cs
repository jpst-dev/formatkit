using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormatKit.App.Localization;
using FormatKit.App.Worker;
using FormatKit.Core;
using FormatKit.Core.Running;

namespace FormatKit.App.ViewModels;

/// <summary>
/// The run and result phases: starts the worker for the reviewed profile, follows its progress, answers its reboot
/// countdown and shows the outcome. Message handling stays on the caller's context (the UI thread in the window), so
/// <see cref="Steps"/> and the reboot dialog are touched only there.
/// </summary>
public sealed partial class RunProfileViewModel
{
    private WorkerConnection? _connection;
    private StepRow? _current;
    private bool _stopSent;
    private bool _stepStarted;
    private string? _refusal;
    private CancellationTokenSource? _startCts;
    private bool _stopOnConnect;
    private TaskCompletionSource _runEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IAsyncRelayCommand RunCommand { get; private set; } = null!;

    /// <summary>Asks the worker to stop after the current step.</summary>
    public IAsyncRelayCommand StopCommand { get; private set; } = null!;

    public IRelayCommand OpenReportCommand { get; private set; } = null!;

    /// <summary>Resumes the interrupted run from the result page (the stop text says "Use Resume").</summary>
    public IAsyncRelayCommand ResumeCommand { get; private set; } = null!;

    public ObservableCollection<StepRow> Steps { get; } = [];

    /// <summary>The final status in plain words (the CLI's run summary texts), or why the run ended.</summary>
    [ObservableProperty]
    private string? _finalStatus;

    [ObservableProperty]
    private string? _reportDirectory;

    /// <summary>Shown on the review when the run did not start (UAC declined, the worker could not start).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRunMessage))]
    private string? _runMessage;

    /// <summary>True from the worker's connection until the run ends.</summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>True when the run state holds an interrupted run, re-read when a run ends (for Resume).</summary>
    [ObservableProperty]
    private bool _hasInterruptedRun;

    public bool HasRunMessage => RunMessage is not null;

    /// <summary>
    /// True while a run starts (the UAC prompt, the worker's connection) or runs: closing the window asks first
    /// (<see cref="ConfirmCloseAsync"/>).
    /// </summary>
    public bool IsRunInProgress => IsBusy || IsRunning;

    public IRelayCommand OpenLogFolderCommand { get; private set; } = null!;

    /// <summary>The last run ended without Finished or Failed (the worker exited unexpectedly): the log folder is offered.</summary>
    [ObservableProperty]
    private bool _endedWithoutResult;

    /// <summary>True from the run's confirmation until it ends, including the UAC prompt and the worker's start.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>A valid profile that enables no step: there is nothing to run.</summary>
    public bool HasNoSteps => ReviewedHash is not null && Plan.Count == 0;

    public bool IsShowingRun => Phase is RunPhase.Running or RunPhase.Result;

    public bool IsResult => Phase == RunPhase.Result;

    private void InitializeRun()
    {
        RunCommand = new AsyncRelayCommand(RunAsync, () => ReviewedHash is not null && HasPlan && !IsRunning);
        StopCommand = new AsyncRelayCommand(StopAsync, () => IsRunning && !_stopSent);
        OpenReportCommand = new RelayCommand(() => _dialogs.OpenFolder(ReportDirectory!), () => ReportDirectory is not null);
        OpenLogFolderCommand = new RelayCommand(() => _dialogs.OpenFolder(AppPaths.Default.LogsDirectory));
        ResumeCommand = new AsyncRelayCommand(ResumeAsync, () => HasInterruptedRun && !IsRunning && !IsBusy);
        RefreshInterruptedRun();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRunInProgress));
        OpenCommand.NotifyCanExecuteChanged();
        UseDriversOnlyCommand.NotifyCanExecuteChanged();
        UseLastProfileCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
        ChooseAnotherCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRunInProgress));
        RunCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasInterruptedRunChanged(bool value) => ResumeCommand?.NotifyCanExecuteChanged();

    partial void OnReportDirectoryChanged(string? value) => OpenReportCommand.NotifyCanExecuteChanged();

    partial void OnPlanChanged(IReadOnlyList<PlanRow> value)
    {
        OnPropertyChanged(nameof(HasNoSteps));
        RunCommand.NotifyCanExecuteChanged();
    }

    partial void OnReviewedHashChanged(string? value)
    {
        OnPropertyChanged(nameof(HasNoSteps));
        RunCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Closing the window: true at once when no run is going on; while a run starts or runs asks to stop after the
    /// current step, and on yes cancels a pending start, sends <see cref="Cancel"/> to a connected worker (also one that
    /// connects despite the cancelled start) and waits until the run ends. False keeps the window open. After Stop was
    /// already sent it does not ask again: it only waits for the run to end.
    /// </summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!IsRunInProgress)
            return true;
        if (!_stopSent && !_stopOnConnect
            && !_dialogs.Confirm(Strings.Get("RunProfile_StopTitle"), Strings.Get("RunProfile_StopQuestion")))
            return false;

        var ended = _runEnded.Task;
        _stopOnConnect = true;
        _startCts?.Cancel();
        if (IsRunning)
            await StopAsync();
        await ended;
        return true;
    }

    /// <summary>
    /// Resumes the interrupted run saved in the run state: the worker reads the profile and the steps from it, so the
    /// step rows come from its <c>planned</c> message. No confirmation: Resume is its own deliberate action, and the
    /// scheduled task starts it unattended. Does nothing while a run is going on.
    /// </summary>
    public async Task ResumeAsync()
    {
        if (IsBusy || IsRunning)
            return;

        IsBusy = true;
        try
        {
            if (!await StartAndFollowAsync(["run", "--resume"], !Elevation.IsElevated, []))
                Phase = RunPhase.Choose;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunAsync()
    {
        if (!_dialogs.Confirm(Strings.Get("RunProfile_ConfirmTitle"), Strings.Get("RunProfile_ConfirmRun")))
            return;

        IsBusy = true;
        try
        {
            await StartAndFollowAsync(
                // The worker parses exactly this job (WorkerHost.TryParse).
                ["run", "--profile", ProfilePath!, "--profile-hash", ReviewedHash!],
                PlanNeedsAdmin && !Elevation.IsElevated,
                Plan.Select(r => new StepRow(r.Id, r.Title)));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Starts the worker and follows it; false when it did not start (the reason is in <see cref="RunMessage"/>, none
    /// when closing the window cancelled the start).
    /// </summary>
    private async Task<bool> StartAndFollowAsync(string[] job, bool elevate, IEnumerable<StepRow> initialRows)
    {
        _runEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _stopOnConnect = false;
        try
        {
            RunMessage = null;
            WorkerConnection? connection;
            using (var start = new CancellationTokenSource())
            {
                _startCts = start;
                try
                {
                    connection = await Launcher.StartAsync(job, elevate, start.Token);
                }
                catch (OperationCanceledException) when (start.IsCancellationRequested)
                {
                    _env.Log.Information("Run: the start was cancelled because the window is closing");
                    return false;
                }
                catch (Exception ex)
                {
                    _env.Log.Error(ex, "Run: the worker could not start");
                    RunMessage = Strings.Format("RunProfile_StartFailed", ex.Message);
                    return false;
                }
                finally
                {
                    _startCts = null;
                }
            }

            if (connection is null)
            {
                RunMessage = Strings.Get("RunProfile_UacDeclined");
                return false;
            }

            await FollowAsync(connection, initialRows);
            return true;
        }
        finally
        {
            _runEnded.TrySetResult();
        }
    }

    /// <summary>Reads the worker's messages until the run ends, then shows the result.</summary>
    private async Task FollowAsync(WorkerConnection connection, IEnumerable<StepRow> initialRows)
    {
        _connection = connection;
        _stopSent = false;
        FinalStatus = null;
        ReportDirectory = null;
        EndedWithoutResult = false;
        _stepStarted = false;
        _refusal = null;
        ShowSteps(initialRows);
        IsRunning = true;
        Phase = RunPhase.Running;

        string? status = null;
        try
        {
            // The window is closing and the worker connected despite the cancelled start: stop it after the current step.
            if (_stopOnConnect)
                await StopAsync();

            await foreach (var message in connection.Channel.ReadAllAsync(CancellationToken.None))
            {
                status = await HandleAsync(message);
                if (status is not null) break;
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            _env.Log.Warning("Run: reading from the worker failed ({Reason})", ex.Message);
        }
        catch (Exception ex)
        {
            // Never leave the page running: the run ends as unexpected, and closing the window stays possible.
            _env.Log.Error(ex, "Run: handling a worker message failed");
        }
        finally
        {
            await EndRunAsync(connection, status);
        }
    }

    private async Task EndRunAsync(WorkerConnection connection, string? status)
    {
        _connection = null;
        try
        {
            await connection.Channel.DisposeAsync();
        }
        catch (Exception ex)
        {
            _env.Log.Warning("Run: closing the worker channel failed ({Reason})", ex.Message);
        }

        if (status is null)
            _env.Log.Warning("Run: the worker channel ended without a result");
        if (_refusal is not null)
        {
            // The worker refused before any step (the profile changed since the review): back to the review, which
            // offers Reload, like a declined UAC prompt.
            RunMessage = _refusal;
            Steps.Clear();
            RefreshInterruptedRun();
            IsRunning = false;
            Phase = RunPhase.Review;
            return;
        }

        EndedWithoutResult = status is null;
        FinalStatus = status ?? Strings.Get("RunProfile_UnexpectedEnd");
        // Also after a stop or a failure: the saved state decides whether Resume is offered.
        RefreshInterruptedRun();
        IsRunning = false;
        Phase = RunPhase.Result;
    }

    /// <summary>Applies one message; returns the final status when the message ends the run.</summary>
    private async Task<string?> HandleAsync(WorkerMessage message)
    {
        switch (message)
        {
            case Planned planned:
                // A resumed run was not reviewed here: its header shows the name the worker sends.
                if (planned.ProfileName is { } name)
                    ProfileName = name;
                ShowSteps(planned.Steps.Select(NewRow));
                return null;
            case StepStarted started:
                _stepStarted = true;
                _current = Find(started.Id);
                if (_current is not null) _current.State = StepState.Running;
                return null;
            case ConsoleOpened:
                if (_current is not null) _current.IsWaitingForConsole = true;
                return null;
            case ConsoleClosed:
                if (_current is not null) _current.IsWaitingForConsole = false;
                return null;
            case ConsoleLine line:
                _current?.AddMessage(line.Text);
                return null;
            case StepFinished finished:
                Find(finished.Id)?.Finish(finished.Outcome, finished.Messages, finished.NextAction);
                return null;
            case RebootCountdown countdown:
                var restart = await _dialogs.RebootCountdownAsync(countdown.Seconds, CancellationToken.None);
                await SendAsync(restart ? new RebootNow() : new RebootCancel());
                return null;
            case Finished done:
                foreach (var report in done.Summary.Steps)
                    Find(report.Id)?.Finish(report.Outcome, report.Messages, report.NextAction);
                ReportDirectory = done.Summary.ReportDirectory;
                return RunStatusText.Of(done.Summary);
            case Failed failed:
                // Shown as it arrives: after a stop it is the worker's Worker_Stopped text, which offers Resume.
                _env.Log.Warning("Run: the worker reported a failure: {Message}", failed.Message);
                if (!_stepStarted && failed.Message == Strings.Get("Worker_ProfileChanged"))
                    _refusal = failed.Message;
                return failed.Message;
            default:
                _env.Log.Warning("Run: unexpected {Type} from the worker ignored", message.GetType().Name);
                return null;
        }
    }

    private async Task StopAsync()
    {
        if (_stopSent) return;
        _stopSent = true;
        StopCommand.NotifyCanExecuteChanged();
        await SendAsync(new Cancel());
    }

    private async Task SendAsync(WorkerMessage message)
    {
        if (_connection is not { } connection) return;
        try
        {
            await connection.Channel.SendAsync(message, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The worker is gone; the read loop sees the end of the channel.
            _env.Log.Warning("Run: could not send {Type} ({Reason})", message.GetType().Name, ex.Message);
        }
    }

    private void ShowSteps(IEnumerable<StepRow> rows)
    {
        _current = null;
        Steps.Clear();
        foreach (var row in rows)
            Steps.Add(row);
    }

    /// <summary>A planned step; one already done before a stop or reboot shows its earlier state.</summary>
    private static StepRow NewRow(PlannedStep step)
    {
        var row = new StepRow(step.Id, step.Title);
        if (step.DoneBefore is { } outcome)
            row.Finish(outcome, [Strings.Get("RunProfile_StepDoneBefore")], null);
        return row;
    }

    private StepRow? Find(string id) => Steps.FirstOrDefault(s => s.Id == id);

    private void RefreshInterruptedRun() =>
        HasInterruptedRun = _env.StateStore.Load().Status == StateLoadStatus.Loaded;
}
