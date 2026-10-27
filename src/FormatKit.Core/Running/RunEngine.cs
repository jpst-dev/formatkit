using FormatKit.Core.Reporting;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Running;

public sealed class RunEngine(
    RunStateStore store,
    IScheduledTasks tasks,
    IRebooter rebooter,
    IReportWriter reports,
    TimeSpan rebootCountdown)
{
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
        foreach (var step in request.Plan)
        {
            if (state.Results.ContainsKey(step.Id))
                continue;

            var result = await RunStepAsync(step, request.Context, ct).ConfigureAwait(false);
            state.Results[step.Id] = new StepRecord(result.Outcome, DateTimeOffset.UtcNow, result.Messages, result.NextAction);
            store.Save(state);

            if (result.Outcome == StepOutcome.Failed && step.IsCritical)
            {
                stoppedOnCritical = true;
                break;
            }

            if (result.Outcome == StepOutcome.RebootRequired)
            {
                await tasks.RegisterResumeTaskAsync(ct).ConfigureAwait(false);
                await rebooter.RebootAsync(rebootCountdown, ct).ConfigureAwait(false);
                return new RunSummary(RunStatus.RebootPending, BuildReports(request, state), null, null);
            }
        }

        var steps = BuildReports(request, state);
        var status = stoppedOnCritical
            ? RunStatus.StoppedOnCriticalFailure
            : steps.Any(s => s.Outcome == StepOutcome.Failed) ? RunStatus.CompletedWithFailures : RunStatus.Completed;

        await tasks.RemoveResumeTaskAsync(ct).ConfigureAwait(false);
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
                return StepResult.Skip();
            return await step.ExecuteAsync(ctx, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StepResult.Fail(null, ex.Message);
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
