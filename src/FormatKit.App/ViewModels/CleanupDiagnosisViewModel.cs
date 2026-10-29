using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormatKit.App.Localization;
using FormatKit.Cleanup.Diagnosis;
using FormatKit.Core;
using FormatKit.Core.Steps;

namespace FormatKit.App.ViewModels;

public sealed record FindingRow(string Title, string Detail, string Breaks, string Fix);

public sealed record FindingGroup(IssueSeverity Severity, string Title, IReadOnlyList<FindingRow> Findings);

/// <summary>The cleanup diagnosis report, grouped by severity (the CLI's <c>cleanup check</c>).</summary>
public sealed partial class CleanupDiagnosisViewModel : QueryPageViewModel
{
    private readonly ServiceSet _services;
    private readonly KnownIssues _issues;
    private readonly Serilog.ILogger? _log;

    public CleanupDiagnosisViewModel(ServiceSet services, KnownIssues issues, Serilog.ILogger? log = null)
        : base(log)
    {
        _services = services;
        _issues = issues;
        _log = log;
        LoadCommand = new AsyncRelayCommand(LoadAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
    }

    public IAsyncRelayCommand LoadCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoFindings))]
    private IReadOnlyList<FindingGroup> _groups = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoFindings))]
    private bool _hasReport;

    /// <summary>Rules whose evaluation threw: their problems, if any, are not in <see cref="Groups"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoFindings), nameof(HasNotChecked), nameof(NotCheckedText))]
    private int _notChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJournalError), nameof(JournalErrorText))]
    private string? _journalError;

    public bool HasNotChecked => NotChecked > 0;

    public string NotCheckedText => HasNotChecked ? Strings.Format("Check_NotChecked", NotChecked) : string.Empty;

    public bool HasJournalError => JournalError is not null;

    public string JournalErrorText => JournalError is { } error ? Strings.Format("Check_JournalError", error) : string.Empty;

    /// <summary>True once a check has run, found nothing and skipped no rule, so the page can say so.</summary>
    public bool NoFindings => HasReport && Groups.Count == 0 && NotChecked == 0;

    private async Task LoadAsync()
    {
        await RunQueryAsync(
            ct => new CleanupDiagnosis(_issues, AppPaths.Default, _log).RunAsync(_services, ct),
            report =>
            {
                Groups = report.Findings
                    .GroupBy(f => f.Issue.Severity)
                    .Select(g => new FindingGroup(g.Key, Strings.Get("Check_Group_" + g.Key), g.Select(ToRow).ToList()))
                    .ToList();
                NotChecked = report.NotChecked;
                JournalError = report.JournalError;
                HasReport = true;
            });

        // A failed refresh must not leave the previous report beside the error.
        if (HasError)
        {
            Groups = [];
            NotChecked = 0;
            JournalError = null;
            HasReport = false;
        }
    }

    private static FindingRow ToRow(Finding finding)
    {
        var text = finding.Issue.LocalizedText();
        return new FindingRow(
            text.Title, finding.Detail, Strings.Format("Check_Breaks", text.Breaks), Strings.Format("Check_Fix", text.Fix));
    }
}
