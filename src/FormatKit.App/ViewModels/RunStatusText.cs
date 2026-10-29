using FormatKit.App.Localization;
using FormatKit.Core.Running;

namespace FormatKit.App.ViewModels;

/// <summary>The final status of a run in plain words: the CLI's run summary texts (RunCommand.Report), copied into App resources.</summary>
public static class RunStatusText
{
    public static string Of(RunSummary summary) => summary.Status switch
    {
        // The worker reports a recorded profile that cannot be used as NothingToResume with the error set (as the CLI does).
        RunStatus.NothingToResume when summary.Error is not null => Strings.Get("Run_ResumeProfileInvalid"),
        RunStatus.NothingToResume => Strings.Get("Run_NothingToResume"),
        RunStatus.StateCorrupt => Strings.Format("Run_StateCorrupt", summary.Error ?? ""),
        RunStatus.ProfileChanged => Strings.Get("Run_ProfileChanged"),
        RunStatus.RebootPending when summary.ResumeScheduleFailed => Strings.Get("Run_ResumeNotScheduled"),
        RunStatus.RebootPending => Strings.Get("Run_RebootPending"),
        RunStatus.StoppedOnCriticalFailure => Strings.Get("Run_Status_StoppedOnCriticalFailure"),
        RunStatus.CompletedWithFailures => Strings.Get("Run_Status_CompletedWithFailures"),
        _ => Strings.Get("Run_Status_Completed"),
    };
}
