using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormatKit.App.Localization;
using FormatKit.App.Services;
using FormatKit.Core.Profiles;
using FormatKit.Hosting;
using FormatKit.Prepare;
using FormatKit.Prepare.Rendering;

namespace FormatKit.App.ViewModels;

public sealed record ItemRow(string Title, string? Detail, string? Action);

public sealed record GroupSection(PrepareGroup Group, IReadOnlyList<ItemRow> Items)
{
    public string Title => ConsoleRenderer.GroupTitle(Group);
}

/// <param name="StartsCollapsed">Ok items are shown folded: they need no attention.</param>
public sealed record StatusGroup(PrepareStatus Status, IReadOnlyList<GroupSection> Sections, bool StartsCollapsed)
{
    public string Title => ConsoleRenderer.StatusTitle(Status);

    public bool StartsExpanded => !StartsCollapsed;
}

/// <summary>The "before you format" checklist (the CLI's <c>prepare check</c>), optionally against a profile.</summary>
public sealed partial class ChecklistViewModel : QueryPageViewModel
{
    private const string ProfileFilter = "JSON (*.json)|*.json|All files (*.*)|*.*";
    private const string MarkdownFilter = "Markdown (*.md)|*.md";
    private const string DefaultFileName = "formatkit-checklist.md";

    private readonly PrepareRunner _runner;
    private readonly ProfileService _profiles;
    private readonly IDialogService _dialogs;
    private PrepareRun? _run;

    public ChecklistViewModel(PrepareRunner runner, ProfileService profiles, IDialogService dialogs,
        Serilog.ILogger? log = null)
        : base(log)
    {
        _runner = runner;
        _profiles = profiles;
        _dialogs = dialogs;
        CheckCommand = new AsyncRelayCommand(CheckAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        SaveMarkdownCommand = new RelayCommand(SaveMarkdown, () => _run is not null);
        BrowseProfileCommand = new RelayCommand(BrowseProfile);
        ClearProfileCommand = new RelayCommand(() => ProfilePath = null);
    }

    public IAsyncRelayCommand CheckCommand { get; }

    public IRelayCommand SaveMarkdownCommand { get; }

    public IRelayCommand BrowseProfileCommand { get; }

    public IRelayCommand ClearProfileCommand { get; }

    [ObservableProperty]
    private string? _profilePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfileErrors))]
    private IReadOnlyList<ProfileError> _profileErrors = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    private IReadOnlyList<StatusGroup> _groups = [];

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveMessage))]
    private string? _saveMessage;

    public bool HasProfileErrors => ProfileErrors.Count > 0;

    public bool HasSaveMessage => SaveMessage is not null;

    public bool HasResults => Groups.Count > 0;

    private sealed record CheckOutcome(IReadOnlyList<ProfileError> Errors, PrepareRun? Run);

    private async Task CheckAsync()
    {
        SaveMessage = null;
        var path = string.IsNullOrWhiteSpace(ProfilePath) ? null : ProfilePath;
        await RunQueryAsync(
            async ct =>
            {
                LoadedProfile? profile = null;
                if (path is not null)
                {
                    var outcome = _profiles.Load(path);
                    if (outcome.Errors.Count > 0 || outcome.Profile is null)
                        return new CheckOutcome(outcome.Errors, null);
                    profile = outcome.Profile;
                }

                return new CheckOutcome([], await _runner.RunAsync(profile, ct));
            },
            outcome =>
            {
                ProfileErrors = outcome.Errors;
                Show(outcome.Run);
            });

        // A failed check must not leave the previous results beside the error.
        if (HasError)
        {
            ProfileErrors = [];
            Show(null);
        }
    }

    private void Show(PrepareRun? run)
    {
        _run = run;
        SaveMarkdownCommand.NotifyCanExecuteChanged();
        if (run is null)
        {
            Groups = [];
            Summary = string.Empty;
            return;
        }

        Groups = run.Report.Items
            .GroupBy(i => i.Status)
            .OrderBy(g => g.Key)
            .Select(status => new StatusGroup(
                status.Key,
                status.GroupBy(i => i.Group)
                    .OrderBy(g => g.Key)
                    .Select(group => new GroupSection(
                        group.Key, group.Select(i => new ItemRow(i.Title, i.Detail, i.Action)).ToList()))
                    .ToList(),
                status.Key == PrepareStatus.Ok))
            .ToList();
        Summary = ConsoleRenderer.Summary(run.Report);
    }

    private void BrowseProfile()
    {
        if (_dialogs.OpenFile(ProfileFilter) is { } path)
            ProfilePath = path;
    }

    private void SaveMarkdown()
    {
        if (_run is null || _dialogs.SaveFile(MarkdownFilter, DefaultFileName) is not { } path)
            return;

        var write = _runner.WriteMarkdown(_run, path);
        if (write.Error is not null)
        {
            SaveMessage = Strings.Format("Prepare_OutputFailed", path, write.Error);
            return;
        }

        SaveMessage = write.OnWindowsDrive
            ? Strings.Format("Prepare_OutputWritten", path) + Environment.NewLine + Strings.Format("Prepare_OutputOnWindows", path)
            : Strings.Format("Prepare_OutputWritten", path);
    }
}
