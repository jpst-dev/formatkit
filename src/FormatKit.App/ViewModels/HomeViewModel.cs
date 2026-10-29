using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormatKit.Core.Running;

namespace FormatKit.App.ViewModels;

public sealed partial class HomeViewModel : ObservableObject
{
    public HomeViewModel(RunStateStore state)
    {
        HasInterruptedRun = state.Load().Status == StateLoadStatus.Loaded;
        GoToCommand = new RelayCommand<Page>(page => NavigationRequested?.Invoke(page));
        // Wired to the run flow in the run-profile task.
        ResumeCommand = new RelayCommand(() => NavigationRequested?.Invoke(Page.RunProfile));
    }

    /// <summary>Raised when the page asks to open another page.</summary>
    public event Action<Page>? NavigationRequested;

    public bool HasInterruptedRun { get; }

    public IRelayCommand ResumeCommand { get; }

    public IRelayCommand<Page> GoToCommand { get; }
}
