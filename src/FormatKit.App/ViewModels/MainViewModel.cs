using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormatKit.App.Localization;

namespace FormatKit.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly Dictionary<Page, object> _pages;
    private readonly HomeViewModel _home;

    public MainViewModel(HomeViewModel home, AboutViewModel about, HardwareViewModel hardware,
        CleanupDiagnosisViewModel diagnosis, ChecklistViewModel checklist,
        CatalogsViewModel catalogs, RunProfileViewModel runProfile, MediaViewModel media)
    {
        _home = home;
        _pages = new Dictionary<Page, object>
        {
            [Page.Home] = home,
            [Page.About] = about,
            [Page.Hardware] = hardware,
            [Page.CleanupDiagnosis] = diagnosis,
            [Page.Checklist] = checklist,
            [Page.Catalogs] = catalogs,
            [Page.RunProfile] = runProfile,
            [Page.Media] = media,
        };
        foreach (var page in Enum.GetValues<Page>().Where(p => !_pages.ContainsKey(p)))
            _pages[page] = new PlaceholderViewModel(page);

        RunProfile = runProfile;
        Media = media;
        NavigateCommand = new RelayCommand<Page>(Navigate);
        // A run can restart the PC, so it never overlaps a media job.
        media.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MediaViewModel.IsRunInProgress))
                runProfile.OtherWorkRunning = media.IsRunInProgress;
        };
        home.NavigationRequested += Navigate;
        home.ResumeRequested += () => _ = ResumeAsync();
        // Home's banner follows the saved run state when a run ends too (the page is not shown during a run).
        runProfile.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunProfileViewModel.HasInterruptedRun))
                home.Refresh();
            else if (e.PropertyName == nameof(RunProfileViewModel.IsRunInProgress))
                media.OtherWorkRunning = runProfile.IsRunInProgress;
        };
        NavItems = Enum.GetValues<Page>()
            .Select(p => new NavItem(p, Strings.Get($"Nav_{p}")))
            .ToList();
        _current = Page.Home;
    }

    /// <summary>The Run profile page, which the window asks before closing during a run.</summary>
    public RunProfileViewModel RunProfile { get; }

    /// <summary>The installation media page, which the window asks before closing during a media job.</summary>
    public MediaViewModel Media { get; }

    /// <summary>True while a run or a media job starts or runs: closing the window asks first.</summary>
    public bool IsWorkInProgress => RunProfile.IsRunInProgress || Media.IsRunInProgress;

    /// <summary>Closing the window: asks the page whose work is going on. Only one can be working.</summary>
    public Task<bool> ConfirmCloseAsync() =>
        Media.IsRunInProgress ? Media.ConfirmCloseAsync() : RunProfile.ConfirmCloseAsync();

    public IReadOnlyList<NavItem> NavItems { get; }

    public IRelayCommand<Page> NavigateCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentViewModel))]
    private Page _current;

    public object CurrentViewModel => _pages[Current];

    partial void OnCurrentChanged(Page oldValue, Page newValue)
    {
        // A page that queries the system stops its query when the user leaves it, and reads again when it opens.
        if (_pages[oldValue] is QueryPageViewModel left)
            left.Leave();
        if (newValue == Page.Home)
            _home.Refresh();
        if (_pages[newValue] is HardwareViewModel hardware)
            hardware.LoadCommand.Execute(null);
        else if (_pages[newValue] is CleanupDiagnosisViewModel diagnosis)
            diagnosis.LoadCommand.Execute(null);
    }

    /// <summary>
    /// Opens Run profile and resumes the interrupted run: Home's Resume, the <c>--resume</c> start after a reboot, and
    /// a <c>--resume</c> start signalled to this window while it was already open. Nothing new starts while a run is
    /// going on (<see cref="RunProfileViewModel.ResumeAsync"/>).
    /// </summary>
    public Task ResumeAsync()
    {
        Navigate(Page.RunProfile);
        return RunProfile.ResumeAsync();
    }

    public void Navigate(Page page) => Current = page;
}
