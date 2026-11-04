using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormatKit.Core.Running;

namespace FormatKit.App.ViewModels;

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly RunStateStore _state;
    private readonly UpdateViewModel? _update;

    public HomeViewModel(RunStateStore state)
    {
        _state = state;
        Refresh();
        GoToCommand = new RelayCommand<Page>(page => NavigationRequested?.Invoke(page));
        ResumeCommand = new RelayCommand(() => ResumeRequested?.Invoke());
    }

    /// <summary>Raised when the page asks to open another page.</summary>
    public event Action<Page>? NavigationRequested;

    /// <summary>Raised when the user clicks Resume: the window opens Run profile and resumes.</summary>
    public event Action? ResumeRequested;

    /// <summary>True when the run state holds an interrupted run.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpdateBanner))]
    private bool _hasInterruptedRun;

    /// <summary>The newer release on offer; null when this window has no updater.</summary>
    public UpdateViewModel? Update
    {
        get => _update;
        init
        {
            _update = value;
            if (value is not null)
            {
                value.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(UpdateViewModel.IsAvailable))
                        OnPropertyChanged(nameof(ShowUpdateBanner));
                };
            }
        }
    }

    /// <summary>An interrupted run comes first, and it would refuse the update anyway.</summary>
    public bool ShowUpdateBanner => Update?.IsAvailable == true && !HasInterruptedRun;

    public IRelayCommand ResumeCommand { get; }

    public IRelayCommand<Page> GoToCommand { get; }

    /// <summary>Reads the run state again: a run may have started, stopped or ended since the last look.</summary>
    public void Refresh() => HasInterruptedRun = _state.Load().Status == StateLoadStatus.Loaded;
}
