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

    /// <summary>True once a check has run and found nothing, so the page can say so.</summary>
    public bool NoFindings => HasReport && Groups.Count == 0;

    private Task LoadAsync() =>
        RunQueryAsync(
            ct => new CleanupDiagnosis(_issues, AppPaths.Default, _log).RunAsync(_services, ct),
            report =>
            {
                Groups = report.Findings
                    .GroupBy(f => f.Issue.Severity)
                    .Select(g => new FindingGroup(g.Key, Strings.Get("Check_Group_" + g.Key), g.Select(ToRow).ToList()))
                    .ToList();
                HasReport = true;
            });

    private static FindingRow ToRow(Finding finding)
    {
        var text = finding.Issue.LocalizedText();
        return new FindingRow(
            text.Title, finding.Detail, Strings.Format("Check_Breaks", text.Breaks), Strings.Format("Check_Fix", text.Fix));
    }
}
