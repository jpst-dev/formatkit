using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormatKit.App.Localization;
using FormatKit.App.Services;
using FormatKit.Core.SystemAccess;
using FormatKit.Hosting;

namespace FormatKit.App.ViewModels;

/// <summary>What Home's banner and the About page show about a newer release.</summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly Updater _updater;
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private ReleaseInfo? _release;
    private CancellationTokenSource? _updateCts;

    public UpdateViewModel(Updater updater, AppSettings settings, IDialogService dialogs)
    {
        _updater = updater;
        _settings = settings;
        _dialogs = dialogs;
        CheckCommand = new AsyncRelayCommand(CheckNowAsync, () => !IsBusy);
        UpdateCommand = new AsyncRelayCommand(UpdateAsync, () => !IsBusy);
    }

    /// <summary>Raised after the files were replaced: the app starts the new version and closes.</summary>
    public event Action? RestartRequested;

    /// <summary>True while a run or a media job goes on; set by the window.</summary>
    public Func<bool> WorkInProgress { get; set; } = () => false;

    public IAsyncRelayCommand CheckCommand { get; }

    public IAsyncRelayCommand UpdateCommand { get; }

    /// <summary>A newer release was found.</summary>
    [ObservableProperty]
    private bool _isAvailable;

    [ObservableProperty]
    private string? _latest;

    [ObservableProperty]
    private Uri? _releasePage;

    /// <summary>The last result to show the user; null when there is nothing to say.</summary>
    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isUpdating;

    public bool IsBusy => IsChecking || IsUpdating;

    /// <summary>The setting of the check at start.</summary>
    public bool CheckForUpdates
    {
        get => _settings.CheckForUpdates;
        set
        {
            _settings.CheckForUpdates = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The check when the window opens: only an offer is ever shown, and a PC without network stays quiet.</summary>
    public async Task CheckOnStartAsync()
    {
        if (!CheckForUpdates || _updater.Refusal() is not null)
            return;

        var found = await CheckAsync();
        if (found.Status == UpdateCheckStatus.Available)
            Offer(found);
    }

    private async Task CheckNowAsync()
    {
        Status = null;
        var found = await CheckAsync();
        switch (found.Status)
        {
            case UpdateCheckStatus.Available:
                Offer(found);
                Status = Strings.Format("Update_Available", found.Latest!);
                break;
            case UpdateCheckStatus.UpToDate:
                IsAvailable = false;
                Status = Strings.Format("Update_UpToDate", found.Current);
                break;
            default:
                Status = Strings.Format("Update_CheckFailed", found.Reason ?? "");
                break;
        }
    }

    private async Task<UpdateCheck> CheckAsync()
    {
        IsChecking = true;
        NotifyCommands();
        try
        {
            return await _updater.CheckAsync(CancellationToken.None);
        }
        finally
        {
            IsChecking = false;
            NotifyCommands();
        }
    }

    private void Offer(UpdateCheck found)
    {
        _release = found.Release;
        Latest = found.Latest;
        ReleasePage = found.Release!.Page;
        IsAvailable = true;
    }

    private async Task UpdateAsync()
    {
        if (_release is null)
            return;

        if (WorkInProgress())
        {
            Status = Strings.Get("Update_WaitForWork");
            return;
        }

        IsUpdating = true;
        NotifyCommands();
        Status = Strings.Get("Update_Downloading");
        UpdateResult result;
        using var cts = new CancellationTokenSource();
        _updateCts = cts;
        try
        {
            result = await _updater.UpdateAsync(_release, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Closing the window stopped it, and the Updater already removed what it had downloaded.
            Status = null;
            return;
        }
        finally
        {
            _updateCts = null;
            IsUpdating = false;
            NotifyCommands();
        }

        if (result.Status == UpdateStatus.Updated)
        {
            Status = null;
            RestartRequested?.Invoke();
            return;
        }

        Status = Strings.Format("Update_Failed", result.Reason ?? "");
    }

    /// <summary>
    /// Closing the window during an update: asks, then stops the download and waits, so the process never ends while
    /// the files are being swapped.
    /// </summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!IsUpdating)
            return true;
        if (!_dialogs.Confirm(Strings.Get("Update_StopTitle"), Strings.Get("Update_StopQuestion")))
            return false;

        _updateCts?.Cancel();
        if (UpdateCommand.ExecutionTask is { } running)
            await running;
        return true;
    }

    private void NotifyCommands()
    {
        CheckCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged();
    }
}
