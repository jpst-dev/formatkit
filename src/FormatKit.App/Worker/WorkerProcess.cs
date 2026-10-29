using FormatKit.Core.Profiles;
using FormatKit.Hosting;

namespace FormatKit.App.Worker;

/// <summary>
/// The worker process's job: accept the window's connection, then run the job through <see cref="WorkerHost"/>.
/// Every failure (no window in time, a taken name, another process connecting, an unexpected error) is logged and
/// returns <see cref="WorkerHost.ExitFailed"/> without running the job.
/// </summary>
public static class WorkerProcess
{
    public static async Task<int> RunAsync(
        HostEnvironment host,
        IReadOnlyList<IModule> modules,
        string pipeName,
        int clientProcessId,
        IReadOnlyList<string> job,
        TimeSpan acceptTimeout,
        CancellationToken ct)
    {
        try
        {
            var stream = await WorkerPipe.AcceptAsync(pipeName, clientProcessId, acceptTimeout, ct).ConfigureAwait(false);
            if (stream is null)
            {
                host.Log.Error("Worker: no window connected to {Pipe} within {Timeout}", pipeName, acceptTimeout);
                return WorkerHost.ExitFailed;
            }

            await using var channel = new MessageChannel(stream, host.Log);
            var rebooter = new PipeRebooter(channel, host.Services.Processes, host.Log);

            // The rebooter is only held by the environment (no service uses it), so swapping it here is enough.
            var env = host with { Rebooter = rebooter };
            var code = await new WorkerHost(env, modules, rebooter).RunAsync(channel, job, ct).ConfigureAwait(false);
            host.Log.Information("Worker: exiting with {ExitCode}", code);
            return code;
        }
        catch (Exception ex)
        {
            host.Log.Error(ex, "Worker: could not run the job on {Pipe}", pipeName);
            return WorkerHost.ExitFailed;
        }
    }
}
