using System.Globalization;
using System.IO;

namespace FormatKit.App.Worker;

/// <summary>Starts the worker for one job and connects to it.</summary>
public interface IWorkerLauncher
{
    /// <summary>Returns null when the user declined the UAC prompt; nothing has started then.</summary>
    Task<WorkerConnection?> StartAsync(IReadOnlyList<string> job, bool elevate, CancellationToken ct);
}

/// <summary>The window's end of a worker: its channel, and its exit code once it exits.</summary>
public sealed record WorkerConnection(MessageChannel Channel, Task<int> Exited);

/// <summary>
/// Starts <c>FormatKitUI.exe --worker --pipe FormatKit-&lt;guid&gt; --client-pid &lt;this pid&gt; &lt;job...&gt;</c> (with <c>runas</c> when
/// <c>elevate</c>) and connects to its pipe. The arguments go as a list, so a path with spaces stays one argument.
/// Throws <see cref="IOException"/> when the worker exits before accepting the connection, and
/// <see cref="TimeoutException"/> when it does not accept within the connect timeout.
/// </summary>
public sealed class WorkerLauncher(
    IElevationStarter starter,
    Serilog.ILogger log,
    string? exePath = null,
    TimeSpan? connectTimeout = null) : IWorkerLauncher
{
    private readonly string _exePath = exePath ?? Environment.ProcessPath!;
    private readonly TimeSpan _connectTimeout = connectTimeout ?? WorkerPipe.AcceptTimeout;

    public async Task<WorkerConnection?> StartAsync(IReadOnlyList<string> job, bool elevate, CancellationToken ct)
    {
        var pipe = "FormatKit-" + Guid.NewGuid().ToString("N");
        // The worker accepts only this process as its client.
        List<string> args = ["--worker", "--pipe", pipe, "--client-pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture), .. job];

        var process = await starter.StartAsync(_exePath, args, elevate).ConfigureAwait(false);
        if (process is null)
        {
            log.Information("Worker: the elevation prompt was declined");
            return null;
        }

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var connecting = WorkerPipe.ConnectAsync(pipe, _connectTimeout, connectCts.Token);
        var first = await Task.WhenAny(connecting, process.Exited).ConfigureAwait(false);
        if (first != connecting)
        {
            // The worker exited: unless it accepted first (a job that failed fast), stop waiting for it.
            await connectCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await connecting.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var code = await process.Exited.ConfigureAwait(false);
                log.Warning("Worker: exited with {Code} before connecting", code);
                throw new IOException($"The worker exited with code {code} before connecting.");
            }
        }

        var stream = await connecting.ConfigureAwait(false);
        log.Information("Worker: connected on {Pipe}", pipe);
        return new WorkerConnection(new MessageChannel(stream, log), process.Exited);
    }
}
