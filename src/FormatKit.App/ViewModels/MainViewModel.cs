using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormatKit.App.Localization;

namespace FormatKit.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly Dictionary<Page, object> _pages;

    public MainViewModel(HomeViewModel home, AboutViewModel about, HardwareViewModel hardware)
    {
        _pages = new Dictionary<Page, object>
        {
            [Page.Home] = home,
            [Page.About] = about,
            [Page.Hardware] = hardware,
        };
        foreach (var page in Enum.GetValues<Page>().Where(p => !_pages.ContainsKey(p)))
            _pages[page] = new PlaceholderViewModel(page);

        NavigateCommand = new RelayCommand<Page>(Navigate);
        home.NavigationRequested += Navigate;
        NavItems = Enum.GetValues<Page>()
            .Where(p => p != Page.Media || IsMediaAvailable)
            .Select(p => new NavItem(p, Strings.Get($"Nav_{p}")))
            .ToList();
        _current = Page.Home;
    }

    /// <summary>The installation-media page stays hidden until it exists.</summary>
    public bool IsMediaAvailable => false;

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
        if (_pages[newValue] is HardwareViewModel hardware)
            hardware.LoadCommand.Execute(null);
    }

    public void Navigate(Page page)
    {
        if (page == Page.Media && !IsMediaAvailable)
            return;
        Current = page;
    }
}
