using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormatKit.App.Services;
using FormatKit.App.Worker;
using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Hosting;

namespace FormatKit.App.ViewModels;

public enum RunPhase { Choose, Review, Running, Result }

/// <summary>One step of the reviewed plan; <paramref name="Items"/> are its cleanup items or programs.</summary>
public sealed record PlanRow(string Id, string Title, bool RequiresAdmin, IReadOnlyList<string> Items)
{
    public bool HasItems => Items.Count > 0;
}

/// <summary>The Run profile page: choose a profile, review its plan, then run it (RunProfileViewModel.Run.cs).</summary>
public sealed partial class RunProfileViewModel : ObservableObject
{
    private const string ProfileFilter = "JSON (*.json)|*.json|All files (*.*)|*.*";

    private readonly ProfileService _profiles;
    private readonly IReadOnlyList<IModule> _modules;
    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings;
    private readonly HostEnvironment _env;

    public RunProfileViewModel(
        ProfileService profiles, IReadOnlyList<IModule> modules, IWorkerLauncher launcher, IElevation elevation,
        IDialogService dialogs, AppSettings settings, HostEnvironment env)
    {
        _profiles = profiles;
        _modules = modules;
        Launcher = launcher;
        Elevation = elevation;
        _dialogs = dialogs;
        _settings = settings;
        _env = env;

        // None of these while a run starts or runs: the page would show another profile than the one running.
        OpenCommand = new RelayCommand(Open, () => !IsBusy);
        UseDriversOnlyCommand = new RelayCommand(() => Review(BuiltInProfiles.DriversOnlyPath), () => !IsBusy);
        UseLastProfileCommand = new RelayCommand(() => Review(LastProfile!), () => LastProfile is not null && !IsBusy);
        ReloadCommand = new RelayCommand(() => Review(ProfilePath!), () => ProfilePath is not null && !IsBusy);
        ChooseAnotherCommand = new RelayCommand(ChooseAnother, () => !IsBusy);
        InitializeRun();
        RefreshLastProfile();
    }

    /// <summary>For the run step: starts the worker.</summary>
    public IWorkerLauncher Launcher { get; }

    public IElevation Elevation { get; }

    public IRelayCommand OpenCommand { get; }

    public IRelayCommand UseDriversOnlyCommand { get; }

    public IRelayCommand UseLastProfileCommand { get; }

    public IRelayCommand ReloadCommand { get; }

    public IRelayCommand ChooseAnotherCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoosing), nameof(IsReviewing), nameof(IsShowingRun), nameof(IsResult))]
    private RunPhase _phase = RunPhase.Choose;

    [ObservableProperty]
    private string? _profilePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    private IReadOnlyList<ProfileError> _errors = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan), nameof(PlanNeedsAdmin))]
    private IReadOnlyList<PlanRow> _plan = [];

    /// <summary>The profile name shown above the plan; null while no valid profile is reviewed.</summary>
    [ObservableProperty]
    private string? _profileName;

    /// <summary>The hash of the reviewed profile; the run step starts the worker with it, and the worker refuses a changed file.</summary>
    [ObservableProperty]
    private string? _reviewedHash;

    /// <summary>The remembered profile when its file still exists, else null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastProfile))]
    private string? _lastProfile;

    public bool IsChoosing => Phase == RunPhase.Choose;

    public bool IsReviewing => Phase == RunPhase.Review;

    public bool HasErrors => Errors.Count > 0;

    public bool HasPlan => Plan.Count > 0;

    public bool HasLastProfile => LastProfile is not null;

    /// <summary>True when a step of the reviewed plan needs administrator rights (the worker is then started elevated).</summary>
    public bool PlanNeedsAdmin => Plan.Any(r => r.RequiresAdmin);

    partial void OnLastProfileChanged(string? value) => UseLastProfileCommand.NotifyCanExecuteChanged();

    partial void OnProfilePathChanged(string? value) => ReloadCommand.NotifyCanExecuteChanged();

    private void Open()
    {
        if (_dialogs.OpenFile(ProfileFilter) is { } path)
            Review(path);
    }

    private void ChooseAnother()
    {
        Phase = RunPhase.Choose;
        RefreshLastProfile();
    }

    private void RefreshLastProfile() =>
        LastProfile = _settings.LastProfile is { Length: > 0 } last && _env.Services.Files.FileExists(last) ? last : null;

    /// <summary>Loads the profile again from disk and shows its plan or its errors.</summary>
    private void Review(string path)
    {
        ProfilePath = path;
        RunMessage = null;
        Phase = RunPhase.Review;
        var outcome = _profiles.Load(path);
        if (outcome.Profile is not { } loaded)
        {
            Show([.. outcome.Errors], [], null, null);
            return;
        }

        try
        {
            var plan = PlanService.Build(loaded.Profile, _modules, _env.SimulateRebootAfterStep)
                .Select(s => new PlanRow(s.Id, s.Title, s.RequiresAdmin, DryRun.Items(s).ToList()))
                .ToList();
            Show([], plan, loaded.Profile.Name, loaded.Hash);
        }
        catch (InvalidOperationException ex)
        {
            // A duplicate step id: the profile cannot be planned.
            Show([new ProfileError(ProfileErrorCode.InvalidValue, string.Empty, ex.Message)], [], null, null);
            return;
        }

        if (path != BuiltInProfiles.DriversOnlyPath)
        {
            _settings.LastProfile = path;
            RefreshLastProfile();
        }
    }

    private void Show(IReadOnlyList<ProfileError> errors, IReadOnlyList<PlanRow> plan, string? name, string? hash)
    {
        Errors = errors;
        Plan = plan;
        ProfileName = name;
        ReviewedHash = hash;
    }
}
