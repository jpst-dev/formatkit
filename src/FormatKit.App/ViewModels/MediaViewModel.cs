using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormatKit.App.Localization;
using FormatKit.App.Services;
using FormatKit.App.Worker;
using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Core.SystemAccess;
using FormatKit.Hosting;
using FormatKit.Media.Building;
using FormatKit.Media.Setup;
using FormatKit.Media.Usb;

namespace FormatKit.App.ViewModels;

public enum MediaStage { Inputs, ChooseDisk, Writing, Result }

/// <summary>
/// One line of the read-only <c>setup</c> section. A class, not a record: the list is replaced with rows that often
/// have the same text, and WPF's item peers go by equality, so equal rows dropped out of the automation tree.
/// </summary>
public sealed class SetupRow(string label, string value)
{
    public string Label { get; } = label;

    public string Value { get; } = value;
}

/// <summary>A disk offered on the page; a disk with a <see cref="Reason"/> cannot be selected.</summary>
public sealed record DiskOption(DiskRow Row)
{
    public string Title { get; } = Strings.Format("Media_DiskTitle", Row.Number, Row.FriendlyName,
        MediaFolder.FormatSize(Row.SizeBytes), Row.BusType, Strings.Get(Row.IsRemovable ? "Media_Removable" : "Media_NotRemovable"));

    public string Volumes { get; } = Row.Volumes.Count == 0
        ? Strings.Get("Media_NoVolumes")
        : string.Join("  ", Row.Volumes.Select(v => v.Letter is { } l ? $"{char.ToUpperInvariant(l)}: {v.Label}".TrimEnd() : v.Label ?? string.Empty));

    public bool IsEligible => Row.Refusal == UsbRefusal.None;

    public string? Reason { get; } = Row.Refusal == UsbRefusal.None
        ? null
        : Strings.Format("Media_Refusal_" + Row.Refusal, string.Join(", ", Row.SourcesOnDisk));
}

/// <summary>
/// The installation media page: the ISO, an optional profile and the target; then the worker's media job (one UAC
/// prompt), the disk choice with the typed number, progress and the result. The page only forwards what was typed: the
/// worker checks it, in <c>FormatKit.Media</c>. Message handling stays on the caller's context (the UI thread in the
/// window).
/// </summary>
public sealed partial class MediaViewModel : ObservableObject
{
    private const string IsoFilter = "ISO (*.iso)|*.iso|All files (*.*)|*.*";
    private const string ProfileFilter = "JSON (*.json)|*.json|All files (*.*)|*.*";

    private readonly ProfileService _profiles;
    private readonly IWorkerLauncher _launcher;
    private readonly IElevation _elevation;
    private readonly IDialogService _dialogs;
    private readonly HostEnvironment _env;
    private readonly MachineLocale _machine;

    private WorkerConnection? _connection;
    private CancellationTokenSource? _startCts;
    private bool _stopOnConnect;
    private bool _confirmSent;
    private bool _stopSent;
    private TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="machine">The host language, keyboard and time zone; null reads them from the registry.</param>
    public MediaViewModel(
        ProfileService profiles, IWorkerLauncher launcher, IElevation elevation, IDialogService dialogs,
        HostEnvironment env, Func<MachineLocale>? machine = null)
    {
        _profiles = profiles;
        _launcher = launcher;
        _elevation = elevation;
        _dialogs = dialogs;
        _env = env;
        _machine = machine?.Invoke() ?? MachineLocale.Current(env.Services.Registry);

        ChooseIsoCommand = new RelayCommand(ChooseIso, () => !IsRunInProgress);
        ChooseProfileCommand = new RelayCommand(ChooseProfile, () => !IsRunInProgress);
        ClearProfileCommand = new RelayCommand(ClearProfile, () => ProfilePath is not null && !IsRunInProgress);
        ChooseFolderCommand = new RelayCommand(ChooseFolder, () => !IsRunInProgress);
        ContinueCommand = new AsyncRelayCommand(ContinueAsync, CanContinue);
        EraseCommand = new AsyncRelayCommand(EraseAsync, CanErase);
        StopCommand = new AsyncRelayCommand(StopAsync, () => _connection is not null && !_stopSent);
        BackCommand = new RelayCommand(Back, () => !IsRunInProgress);
        OpenLogFolderCommand = new RelayCommand(() => _dialogs.OpenFolder(AppPaths.Default.LogsDirectory));
        ShowSetup(new SetupOptions());
    }

    public IRelayCommand ChooseIsoCommand { get; }

    public IRelayCommand ChooseProfileCommand { get; }

    public IRelayCommand ClearProfileCommand { get; }

    public IRelayCommand ChooseFolderCommand { get; }

    public IAsyncRelayCommand ContinueCommand { get; }

    /// <summary>Sends the selected disk and what was typed; the worker decides.</summary>
    public IAsyncRelayCommand EraseCommand { get; }

    /// <summary>During the disk choice nothing was erased yet; during the writing the target ends up incomplete.</summary>
    public IAsyncRelayCommand StopCommand { get; }

    public IRelayCommand BackCommand { get; }

    public IRelayCommand OpenLogFolderCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInputs), nameof(IsChoosingDisk), nameof(IsWriting), nameof(IsResult))]
    private MediaStage _stage = MediaStage.Inputs;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private string? _isoPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearProfileCommand))]
    private string? _profilePath;

    [ObservableProperty]
    private string? _profileName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfileErrors))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private IReadOnlyList<ProfileError> _profileErrors = [];

    [ObservableProperty]
    private IReadOnlyList<SetupRow> _setupRows = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolderTarget))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _isUsbTarget = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private string? _outputFolder;

    [ObservableProperty]
    private bool _allowHardDisk;

    /// <summary>Why the job did not start or was refused: UAC declined, the worker's checks.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    /// <summary>True from Continue until the job ends, including the UAC prompt and the worker's start.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunInProgress))]
    private bool _isBusy;

    /// <summary>A profile run is going on: it can restart the PC, so no media job may start.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _otherWorkRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EraseWarning), nameof(IsHardDiskSelected), nameof(HasDiskSelected))]
    [NotifyCanExecuteChangedFor(nameof(EraseCommand))]
    private DiskOption? _selectedDisk;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EraseCommand))]
    private string _typedNumber = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoDisks))]
    private int _diskCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHiddenHardDisks))]
    private string? _hiddenHardDisksText;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private int _percent;

    [ObservableProperty]
    private string? _finalStatus;

    [ObservableProperty]
    private bool _showChecklistReminder;

    /// <summary>The worker ended without a result: the log folder is offered.</summary>
    [ObservableProperty]
    private bool _endedWithoutResult;

    public ObservableCollection<DiskOption> Disks { get; } = [];

    /// <summary>Lines the worker wrote for the user, such as a failed dismount.</summary>
    public ObservableCollection<string> Notes { get; } = [];

    public bool IsInputs => Stage == MediaStage.Inputs;

    public bool IsChoosingDisk => Stage == MediaStage.ChooseDisk;

    public bool IsWriting => Stage == MediaStage.Writing;

    public bool IsResult => Stage == MediaStage.Result;

    public bool HasProfileErrors => ProfileErrors.Count > 0;

    public bool HasMessage => Message is not null;

    public bool HasNoDisks => DiskCount == 0;

    public bool HasHiddenHardDisks => HiddenHardDisksText is not null;

    public bool HasDiskSelected => SelectedDisk is not null;

    public bool IsHardDiskSelected => SelectedDisk is { Row.IsRemovable: false };

    public string? EraseWarning => SelectedDisk is { } disk ? Strings.Format("Media_EraseWarning", disk.Row.Number) : null;

    /// <summary>The folder target; the opposite of <see cref="IsUsbTarget"/>, for the second radio button.</summary>
    public bool IsFolderTarget
    {
        get => !IsUsbTarget;
        set => IsUsbTarget = !value;
    }

    /// <summary>True while a media job starts or runs: closing the window asks first (<see cref="ConfirmCloseAsync"/>).</summary>
    public bool IsRunInProgress => IsBusy;

    private string? ReviewedHash { get; set; }

    partial void OnIsBusyChanged(bool value)
    {
        ChooseIsoCommand.NotifyCanExecuteChanged();
        ChooseProfileCommand.NotifyCanExecuteChanged();
        ClearProfileCommand.NotifyCanExecuteChanged();
        ChooseFolderCommand.NotifyCanExecuteChanged();
        ContinueCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDiskChanged(DiskOption? value) => TypedNumber = string.Empty;

    private bool CanContinue() =>
        !IsRunInProgress && !OtherWorkRunning && IsoPath is not null && !HasProfileErrors && (IsUsbTarget || OutputFolder is not null);

    private bool CanErase() =>
        Stage == MediaStage.ChooseDisk && SelectedDisk is { IsEligible: true } && !string.IsNullOrWhiteSpace(TypedNumber) && !_stopSent;

    private void ChooseIso()
    {
        if (_dialogs.OpenFile(IsoFilter) is { } path)
            IsoPath = path;
    }

    private void ChooseFolder()
    {
        if (_dialogs.PickFolder() is { } path)
            OutputFolder = path;
    }

    private void ChooseProfile()
    {
        if (_dialogs.OpenFile(ProfileFilter) is not { } path)
            return;

        ProfilePath = path;
        var outcome = _profiles.Load(path);
        if (outcome.Profile is not { } loaded)
        {
            ShowProfile(null, null, outcome.Errors, new SetupOptions());
            return;
        }

        SetupOptions? options;
        IReadOnlyList<ProfileError> errors;
        try
        {
            (options, errors) = SetupSectionParser.Parse(_env.Services.Files.ReadAllText(loaded.Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _env.Log.Warning("Media: could not read the profile {Path} ({Reason})", path, ex.Message);
            (options, errors) = (null, [new ProfileError(ProfileErrorCode.InvalidValue, string.Empty, ex.Message)]);
        }

        if (options is null)
        {
            ShowProfile(null, null, errors, new SetupOptions());
            return;
        }

        ShowProfile(loaded.Profile.Name, loaded.Hash, [], options);
    }

    private void ClearProfile()
    {
        ProfilePath = null;
        ShowProfile(null, null, [], new SetupOptions());
    }

    private void ShowProfile(string? name, string? hash, IReadOnlyList<ProfileError> errors, SetupOptions setup)
    {
        ProfileName = name;
        ReviewedHash = hash;
        ProfileErrors = errors;
        Message = null;
        ShowSetup(setup);
    }

    /// <summary>The <c>setup</c> choices, with what applies to each field the profile leaves out (SetupDefaults).</summary>
    private void ShowSetup(SetupOptions o) => SetupRows =
    [
        new(Strings.Get("Media_Setup_Account"), Strings.Get(o.Account == SetupAccount.Local ? "Media_Setup_Account_Local" : "Media_Setup_Account_Microsoft")),
        new(Strings.Get("Media_Setup_Edition"), o.Edition ?? Strings.Get("Media_Setup_EditionDefault")),
        new(Strings.Get("Media_Setup_ComputerName"), o.ComputerName ?? Strings.Get("Media_Setup_ComputerNameDefault")),
        new(Strings.Get("Media_Setup_Language"), o.Language ?? Strings.Format("Media_Setup_LanguageDefault", _machine.UiLanguage)),
        new(Strings.Get("Media_Setup_Keyboard"), o.Keyboard ?? (_machine.Keyboard is { } keyboard
            ? Strings.Format("Media_Setup_KeyboardDefault", keyboard)
            : Strings.Get("Media_Setup_KeyboardLanguageDefault"))),
        new(Strings.Get("Media_Setup_TimeZone"), o.TimeZone ?? Strings.Format("Media_Setup_TimeZoneDefault", _machine.TimeZone)),
    ];

    private async Task ContinueAsync()
    {
        var job = new MediaJobRequest(IsoPath!, ProfilePath, ReviewedHash, IsUsbTarget ? null : OutputFolder, IsUsbTarget && AllowHardDisk);
        _ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _stopOnConnect = false;
        IsBusy = true;
        Message = null;
        try
        {
            WorkerConnection? connection;
            using (var start = new CancellationTokenSource())
            {
                _startCts = start;
                try
                {
                    connection = await _launcher.StartAsync(job.ToArguments(), !_elevation.IsElevated, start.Token);
                }
                catch (OperationCanceledException) when (start.IsCancellationRequested)
                {
                    _env.Log.Information("Media: the start was cancelled because the window is closing");
                    return;
                }
                catch (Exception ex)
                {
                    _env.Log.Error(ex, "Media: the worker could not start");
                    Message = Strings.Format("RunProfile_StartFailed", ex.Message);
                    return;
                }
                finally
                {
                    _startCts = null;
                }
            }

            if (connection is null)
            {
                Message = Strings.Get("RunProfile_UacDeclined");
                return;
            }

            await FollowAsync(connection);
        }
        finally
        {
            IsBusy = false;
            _ended.TrySetResult();
        }
    }

    /// <summary>Reads the worker's messages until the job ends, then shows the result.</summary>
    private async Task FollowAsync(WorkerConnection connection)
    {
        _connection = connection;
        _stopSent = false;
        _confirmSent = false;
        Disks.Clear();
        Notes.Clear();
        SelectedDisk = null;
        FinalStatus = null;
        EndedWithoutResult = false;
        ShowChecklistReminder = false;
        Percent = 0;
        StatusText = Strings.Get("Media_Preparing");
        Stage = MediaStage.Writing;
        StopCommand.NotifyCanExecuteChanged();

        var ended = false;
        try
        {
            // The window is closing and the worker connected despite the cancelled start.
            if (_stopOnConnect)
                await StopAsync();

            await foreach (var message in connection.Channel.ReadAllAsync(CancellationToken.None))
            {
                ended = Handle(message);
                if (ended) break;
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            _env.Log.Warning("Media: reading from the worker failed ({Reason})", ex.Message);
        }
        catch (Exception ex)
        {
            // Never leave the page working: the job ends as unexpected, and closing the window stays possible.
            _env.Log.Error(ex, "Media: handling a worker message failed");
        }
        finally
        {
            _connection = null;
            StopCommand.NotifyCanExecuteChanged();
            try
            {
                await connection.Channel.DisposeAsync();
            }
            catch (Exception ex)
            {
                _env.Log.Warning("Media: closing the worker channel failed ({Reason})", ex.Message);
            }

            if (!ended)
                ShowUnexpectedEnd();
        }
    }

    /// <summary>Applies one message; true when it ends the job.</summary>
    private bool Handle(WorkerMessage message)
    {
        switch (message)
        {
            case Disks disks:
                ShowDisks(disks);
                return false;
            case MediaProgressUpdate progress:
                StatusText = Strings.Get("Media_Phase_" + progress.Phase);
                Percent = progress.TotalBytes <= 0 ? 0 : (int)Math.Min(100, progress.CopiedBytes * 100 / progress.TotalBytes);
                return false;
            case ConsoleLine line:
                Notes.Add(line.Text);
                return false;
            case MediaFinished finished:
                _env.Log.Information("Media: the job ended with {Status}", finished.Status);
                FinalStatus = finished.Message;
                ShowChecklistReminder = finished.Status == MediaFinishedStatus.Done;
                Stage = MediaStage.Result;
                return true;
            case Failed failed:
                // Refused before anything was erased or written: back to the inputs, with the choices kept.
                _env.Log.Warning("Media: the worker refused the job ({Reason}): {Message}", failed.Reason, failed.Message);
                Message = failed.Message;
                Stage = MediaStage.Inputs;
                return true;
            default:
                _env.Log.Warning("Media: unexpected {Type} from the worker ignored", message.GetType().Name);
                return false;
        }
    }

    private void ShowDisks(Disks disks)
    {
        Disks.Clear();
        foreach (var row in disks.Items)
            Disks.Add(new DiskOption(row));
        DiskCount = Disks.Count;
        HiddenHardDisksText = disks.HiddenHardDisks > 0 ? Strings.Format("Media_HardDisksHidden", disks.HiddenHardDisks) : null;
        Stage = MediaStage.ChooseDisk;
        EraseCommand.NotifyCanExecuteChanged();
    }

    private void ShowUnexpectedEnd()
    {
        _env.Log.Warning("Media: the worker channel ended without a result");
        EndedWithoutResult = true;
        FinalStatus = !IsUsbTarget
            ? Strings.Format("Media_EndedFolder", OutputFolder!)
            : Strings.Get(_confirmSent ? "Media_EndedAfterErase" : "Media_EndedBeforeErase");
        Stage = MediaStage.Result;
    }

    private async Task EraseAsync()
    {
        _confirmSent = true;
        var choice = new ConfirmDisk(SelectedDisk!.Row.UniqueId, TypedNumber);
        StatusText = Strings.Get("Media_Erasing");
        Percent = 0;
        Stage = MediaStage.Writing;
        EraseCommand.NotifyCanExecuteChanged();
        await SendAsync(choice);
    }

    private async Task StopAsync()
    {
        if (_stopSent) return;
        _stopSent = true;
        StopCommand.NotifyCanExecuteChanged();
        EraseCommand.NotifyCanExecuteChanged();
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
            _env.Log.Warning("Media: could not send {Type} ({Reason})", message.GetType().Name, ex.Message);
        }
    }

    private void Back()
    {
        Stage = MediaStage.Inputs;
        ContinueCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Closing the window: true at once when no job is going on; otherwise asks, and on yes cancels a pending start,
    /// sends <see cref="Cancel"/> to a connected worker and waits until the job ends. False keeps the window open.
    /// </summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!IsRunInProgress)
            return true;
        if (!_stopSent && !_stopOnConnect
            && !_dialogs.Confirm(Strings.Get("Media_StopTitle"), Strings.Get("Media_StopQuestion")))
            return false;

        var ended = _ended.Task;
        _stopOnConnect = true;
        try
        {
            _startCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The start ended between the read and the cancel.
        }

        if (_connection is not null)
            await StopAsync();
        await ended;
        return true;
    }
}
