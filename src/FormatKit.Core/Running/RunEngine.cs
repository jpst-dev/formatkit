using FormatKit.Core.Localization;
using FormatKit.Core.Reporting;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Running;

public sealed class RunEngine(
    RunStateStore store,
    IScheduledTasks tasks,
    IRebooter rebooter,
    IReportWriter reports,
    TimeSpan rebootCountdown,
    Serilog.ILogger? log = null,
    IRunProgress? progress = null,
    Func<DateTimeOffset>? lastBootTime = null)
{
    private readonly Func<DateTimeOffset> _lastBootTime =
        lastBootTime ?? (() => DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64));

    public async Task<RunSummary> RunAsync(RunRequest request, CancellationToken ct)
    {
        RunState state;
        if (request.Resume)
        {
            var load = store.Load();
            switch (load.Status)
            {
                case StateLoadStatus.None:
                    return Early(RunStatus.NothingToResume, null);
                case StateLoadStatus.Corrupt:
                    return Early(RunStatus.StateCorrupt, load.Error);
            }

            state = load.State!;
            if (!string.Equals(state.ProfileHash, request.ProfileHash, StringComparison.Ordinal))
                return Early(RunStatus.ProfileChanged, null);

            MarkRebootedSteps(state);
        }
        else
        {
            state = new RunState(
                request.ProfilePath,
                request.ProfileHash,
                request.Plan.Select(s => s.Id).ToList(),
                new Dictionary<string, StepRecord>(),
                DateTimeOffset.UtcNow);
            store.Save(state);
        }

        var stoppedOnCritical = false;
        for (var i = 0; i < request.Plan.Count; i++)
        {
            var step = request.Plan[i];
            if (state.Results.ContainsKey(step.Id))
                continue;

            progress?.StepStarted(step, i + 1, request.Plan.Count);
            var result = await RunStepAsync(step, request.Context, ct).ConfigureAwait(false);
            progress?.StepFinished(step, result);
            state.Results[step.Id] = new StepRecord(result.Outcome, DateTimeOffset.UtcNow, result.Messages, result.NextAction);
            store.Save(state);

            if (result.Outcome == StepOutcome.Failed && step.IsCritical)
            {
                stoppedOnCritical = true;
                break;
            }

            if (result.Outcome == StepOutcome.RebootRequired)
            {
                var scheduleFailed = false;
                try
                {
                    await tasks.RegisterResumeTaskAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // State is already saved; the user can resume manually after the reboot.
                    scheduleFailed = true;
                    log?.Warning(ex, "Could not register the resume scheduled task");
                }

                await rebooter.RebootAsync(rebootCountdown, ct).ConfigureAwait(false);
                return new RunSummary(RunStatus.RebootPending, BuildReports(request, state), null, null, scheduleFailed);
            }
        }

        var steps = BuildReports(request, state);
        var status = stoppedOnCritical
            ? RunStatus.StoppedOnCriticalFailure
            : steps.Any(s => s.Outcome == StepOutcome.Failed) ? RunStatus.CompletedWithFailures : RunStatus.Completed;

        try
        {
            await tasks.RemoveResumeTaskAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.Warning(ex, "Could not remove the resume scheduled task");
        }

        var summary = new RunSummary(status, steps, null, null);
        var dir = reports.Write(summary);
        store.Delete();
        return summary with { ReportDirectory = dir };
    }

    private static async Task<StepResult> RunStepAsync(IStep step, StepContext ctx, CancellationToken ct)
    {
        try
        {
            if (await step.CheckAsync(ctx, ct).ConfigureAwait(false))
                return StepResult.Skip(Strings.Get("Step_AlreadyInPlace"));
            return await step.ExecuteAsync(ctx, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StepResult.Fail(null, ex.Message);
        }
    }

    /// <summary>A step that asked for a reboot is done once the machine has actually restarted since it finished.</summary>
    private void MarkRebootedSteps(RunState state)
    {
        var lastBoot = _lastBootTime();
        foreach (var (id, record) in state.Results.ToList())
        {
            if (record.Outcome == StepOutcome.RebootRequired && record.FinishedAt < lastBoot)
                state.Results[id] = record with
                {
                    Outcome = StepOutcome.Succeeded,
                    Messages = [.. record.Messages, Strings.Get("Step_DoneAfterReboot")],
                };
        }
    }

    private static RunSummary Early(RunStatus status, string? error) => new(status, [], null, error);

    private static List<StepReport> BuildReports(RunRequest request, RunState state) =>
        request.Plan
            .Where(s => state.Results.ContainsKey(s.Id))
            .Select(s =>
            {
                var r = state.Results[s.Id];
                return new StepReport(s.Id, s.Title, r.Outcome, r.Messages, r.NextAction);
            })
            .ToList();
}
