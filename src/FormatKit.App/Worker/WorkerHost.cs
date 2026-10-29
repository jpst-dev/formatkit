using System.IO;
using System.Text;
using FormatKit.App.Localization;
using FormatKit.Core.Profiles;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using FormatKit.Hosting;

namespace FormatKit.App.Worker;

/// <summary>
/// The worker's run: loads the profile (refusing one whose hash differs from the reviewed one, D7), sends the plan,
/// runs it through <see cref="RunSession"/> and reports over the channel. A <see cref="Cancel"/> from the window or the
/// end of the stream stops the run after the current step; reboot replies go to <paramref name="rebooter"/>, which must
/// be the rebooter in <paramref name="env"/>. Jobs: <c>run --profile &lt;path&gt; --profile-hash &lt;hash&gt;</c> and
/// <c>run --resume</c>. <see cref="RunAsync"/> returns 0 after sending <see cref="Finished"/>, 2 after <see cref="Failed"/>.
/// </summary>
public sealed class WorkerHost(HostEnvironment env, IReadOnlyList<IModule> modules, PipeRebooter rebooter)
{
    public const int ExitFinished = 0;
    public const int ExitFailed = 2;

    private readonly TaskCompletionSource _stopRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the read loop asks the run to stop (tests wait on it).</summary>
    internal Task StopRequested => _stopRequested.Task;

    public async Task<int> RunAsync(MessageChannel channel, IReadOnlyList<string> job, CancellationToken ct)
    {
        using var stop = new CancellationTokenSource();
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reading = ReadLoopAsync(channel, stop, readCts.Token);
        try
        {
            return await RunJobAsync(channel, job, stop.Token, ct).ConfigureAwait(false);
        }
        finally
        {
            await readCts.CancelAsync().ConfigureAwait(false);
            await reading.ConfigureAwait(false);
        }
    }

    private async Task<int> RunJobAsync(MessageChannel channel, IReadOnlyList<string> job, CancellationToken stop, CancellationToken ct)
    {
        if (!TryParse(job, out var path, out var hash, out var resume))
            return await FailAsync(channel, Strings.Format("Worker_InvalidJob", string.Join(' ', job)), ct).ConfigureAwait(false);

        var session = new RunSession(env);
        StateLoadResult? load = null;
        if (resume)
        {
            load = env.StateStore.Load();
            switch (load.Status)
            {
                case StateLoadStatus.None:
                    return await EndResumeAsync(channel, session, RunStatus.NothingToResume, null, ct).ConfigureAwait(false);
                case StateLoadStatus.Corrupt:
                    return await EndResumeAsync(channel, session, RunStatus.StateCorrupt, load.Error, ct).ConfigureAwait(false);
            }

            path = load.State!.ProfilePath;
        }

        var outcome = new ProfileService(modules).Load(path!);
        if (outcome.Profile is null)
        {
            env.Log.Warning("Worker: profile {Path} is not valid ({Count} problems)", path, outcome.Errors.Count);
            return resume
                ? await EndResumeAsync(channel, session, RunStatus.NothingToResume, ErrorLines(outcome.Errors), ct).ConfigureAwait(false)
                : await FailAsync(channel, Strings.Format("Worker_ProfileErrors", outcome.Errors.Count) + "\n" + ErrorLines(outcome.Errors), ct).ConfigureAwait(false);
        }

        var profile = outcome.Profile;
        var done = load?.State?.Results;
        if (!resume && !string.Equals(profile.Hash, hash, StringComparison.Ordinal))
        {
            env.Log.Warning("Worker: profile {Path} changed since it was reviewed", path);
            return await FailAsync(channel, Strings.Get("Worker_ProfileChanged"), ct).ConfigureAwait(false);
        }

        var plan = PlanService.Build(profile.Profile, modules, env.SimulateRebootAfterStep);
        await TrySendAsync(channel, new Planned(plan.Select(s => new PlannedStep(s.Id, s.Title, s.RequiresAdmin, DoneBefore(done, s.Id))).ToList()), ct).ConfigureAwait(false);

        // Steps get the host token, never the stop token, so a stop never interrupts a step mid-work.
        // Every send below waits for the posted progress messages, so the window sees them in order.
        var progress = new PipeRunProgress(channel, stop);
        RunSummary summary;
        try
        {
            summary = await session.RunAsync(profile, plan, resume, progress, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the host is shutting down
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            env.Log.Information("Worker: run stopped after the current step");
            return await FailAsync(channel, Strings.Get("Worker_Stopped"), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Includes a cancellation neither token asked for, e.g. an HTTP timeout surfacing from a step.
            env.Log.Error(ex, "Worker: the run failed");
            return await FailAsync(channel, Strings.Format("Worker_Error", ex.Message), ct).ConfigureAwait(false);
        }

        await TrySendAsync(channel, new Finished(summary), ct).ConfigureAwait(false);
        return ExitFinished;
    }

    /// <summary>
    /// The outcome the saved run recorded for a step the engine will skip on resume. A step that asked for a reboot is
    /// left out: the engine decides after the restart whether it is done or runs again, and the final result reports it.
    /// </summary>
    private static StepOutcome? DoneBefore(IReadOnlyDictionary<string, StepRecord>? results, string id) =>
        results is not null && results.TryGetValue(id, out var record) && record.Outcome != StepOutcome.RebootRequired
            ? record.Outcome
            : null;

    /// <summary>A resume that cannot go on: removes the resume task as the CLI does and reports the status.</summary>
    private async Task<int> EndResumeAsync(MessageChannel channel, RunSession session, RunStatus status, string? error, CancellationToken ct)
    {
        await session.RemoveResumeTaskAsync(ct).ConfigureAwait(false);
        await TrySendAsync(channel, new Finished(new RunSummary(status, [], null, error)), ct).ConfigureAwait(false);
        return ExitFinished;
    }

    private async Task<int> FailAsync(MessageChannel channel, string message, CancellationToken ct)
    {
        await TrySendAsync(channel, new Failed(message), ct).ConfigureAwait(false);
        return ExitFailed;
    }

    private async Task TrySendAsync(MessageChannel channel, WorkerMessage message, CancellationToken ct)
    {
        try
        {
            await channel.SendAsync(message, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            env.Log.Warning("Worker channel: could not send {Type} ({Reason})", message.GetType().Name, ex.Message);
        }
    }

    /// <summary>Reads the window's messages until the run ends; <see cref="Cancel"/> or the end of the stream stops the run.</summary>
    private async Task ReadLoopAsync(MessageChannel channel, CancellationTokenSource stop, CancellationToken ct)
    {
        await Task.Yield();
        try
        {
            await foreach (var message in channel.ReadAllAsync(ct).ConfigureAwait(false))
            {
                switch (message)
                {
                    case Cancel:
                        env.Log.Information("Worker: the window asked to stop after the current step");
                        RequestStop(stop);
                        break;
                    case RebootNow or RebootCancel:
                        rebooter.Deliver(message);
                        break;
                    default:
                        env.Log.Warning("Worker channel: unexpected {Type} ignored", message.GetType().Name);
                        break;
                }
            }

            env.Log.Information("Worker: the window closed the connection; stopping after the current step");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return; // the run is over
        }
        catch (Exception ex)
        {
            // Never let the read loop fail the run's result: any error ends reading and stops the run.
            env.Log.Warning(ex, "Worker channel: reading failed; stopping after the current step");
        }

        RequestStop(stop);
    }

    private void RequestStop(CancellationTokenSource stop)
    {
        stop.Cancel();
        _stopRequested.TrySetResult();
    }

    private static bool TryParse(IReadOnlyList<string> job, out string? path, out string? hash, out bool resume)
    {
        path = hash = null;
        resume = false;
        if (job.Count == 2 && job[0] == "run" && job[1] == "--resume")
        {
            resume = true;
            return true;
        }

        if (job.Count != 5 || job[0] != "run") return false;
        for (var i = 1; i < 5; i += 2)
        {
            switch (job[i])
            {
                case "--profile" when path is null: path = job[i + 1]; break;
                case "--profile-hash" when hash is null: hash = job[i + 1]; break;
                default: return false;
            }
        }

        return path is not null && hash is not null;
    }

    private static string ErrorLines(IReadOnlyList<ProfileError> errors)
    {
        var text = new StringBuilder();
        foreach (var error in errors)
        {
            if (text.Length > 0) text.Append('\n');
            text.Append(error.Path.Length == 0 ? error.Message : $"{error.Path}: {error.Message}");
        }

        return text.ToString();
    }
}
