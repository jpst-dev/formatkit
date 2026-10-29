using FormatKit.Core.Profiles;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;

namespace FormatKit.Hosting;

/// <summary>Runs a plan through the engine for any host and cleans up the resume task when a resume ends early.</summary>
public sealed class RunSession(HostEnvironment env)
{
    /// <summary>Whether the last <see cref="RunAsync"/> removed the resume task because the resume ended early.</summary>
    public bool ResumeTaskRemoved { get; private set; }

    public async Task<RunSummary> RunAsync(
        LoadedProfile profile, IReadOnlyList<IStep> plan, bool resume, IRunProgress progress, CancellationToken ct)
    {
        ResumeTaskRemoved = false;
        var engine = new RunEngine(
            env.StateStore, env.ScheduledTasks, env.Rebooter, env.Reports, env.RebootCountdown, env.Log, progress);
        var request = new RunRequest(profile.Path, profile.Hash, plan, new StepContext(env.Services, env.Log), resume);

        var summary = await engine.RunAsync(request, ct).ConfigureAwait(false);

        if (resume && summary.Status is RunStatus.NothingToResume or RunStatus.StateCorrupt or RunStatus.ProfileChanged)
        {
            ResumeTaskRemoved = await RemoveResumeTaskAsync(ct).ConfigureAwait(false);
        }

        return summary;
    }

    /// <summary>Best-effort removal of the automatic resume task; returns whether one was removed.</summary>
    public async Task<bool> RemoveResumeTaskAsync(CancellationToken ct)
    {
        try
        {
            return await env.ScheduledTasks.RemoveResumeTaskAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            env.Log.Warning(ex, "Could not remove the resume scheduled task");
            return false;
        }
    }
}
