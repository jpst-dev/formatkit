using FormatKit.Core.Steps;

namespace FormatKit.Core.Running;

public enum RunStatus
{
    Completed,
    CompletedWithFailures,
    StoppedOnCriticalFailure,
    RebootPending,
    ProfileChanged,
    NothingToResume,
    StateCorrupt,
}

public sealed record StepReport(
    string Id,
    string Title,
    StepOutcome Outcome,
    IReadOnlyList<string> Messages,
    string? NextAction);

public sealed record RunSummary(
    RunStatus Status,
    IReadOnlyList<StepReport> Steps,
    string? ReportDirectory,
    string? Error,
    bool ResumeScheduleFailed = false);
