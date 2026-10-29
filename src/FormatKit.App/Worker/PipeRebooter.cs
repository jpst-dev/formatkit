using System.IO;
using FormatKit.Core.SystemAccess;

namespace FormatKit.App.Worker;

/// <summary>
/// The worker's rebooter: sends <see cref="RebootCountdown"/> and waits for the window's <see cref="RebootNow"/>,
/// <see cref="RebootCancel"/> or the end of the countdown. Replies arrive through <see cref="Deliver"/>, called by the
/// host's read loop. If the window is gone the countdown still runs out and the machine restarts, as unattended
/// in the CLI.
/// </summary>
public sealed class PipeRebooter(MessageChannel channel, IProcessRunner runner, Serilog.ILogger log) : IRebooter
{
    private TaskCompletionSource<bool>? _reply;

    public async Task<bool> RebootAsync(TimeSpan countdown, CancellationToken ct)
    {
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _reply, reply);
        try
        {
            try
            {
                await channel.SendAsync(new RebootCountdown((int)Math.Ceiling(countdown.TotalSeconds)), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                log.Warning("Worker channel: could not send the reboot countdown ({Reason})", ex.Message);
            }

            using var elapsed = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var delay = Task.Delay(countdown, elapsed.Token);
            var first = await Task.WhenAny(reply.Task, delay).ConfigureAwait(false);
            await elapsed.CancelAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            if (first == reply.Task && !reply.Task.Result)
            {
                log.Information("Reboot cancelled from the window");
                return false;
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref _reply, null, reply);
        }

        return await SystemRestart.RestartAsync(runner, log, ct).ConfigureAwait(false);
    }

    /// <summary>Hands a window reply to the pending countdown; ignored when no countdown is waiting.</summary>
    public void Deliver(WorkerMessage reply)
    {
        var pending = Volatile.Read(ref _reply);
        switch (reply)
        {
            case RebootNow:
                pending?.TrySetResult(true);
                break;
            case RebootCancel:
                pending?.TrySetResult(false);
                break;
        }
    }
}
