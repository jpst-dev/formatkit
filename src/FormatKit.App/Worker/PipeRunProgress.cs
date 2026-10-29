using System.IO;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;

namespace FormatKit.App.Worker;

/// <summary>
/// Sends <see cref="StepStarted"/> and <see cref="StepFinished"/> to the window, in order and without blocking the
/// engine (<see cref="DrainAsync"/> waits for them). When <paramref name="stopBeforeNextStep"/> is cancelled, the next
/// <see cref="StepStarted"/> throws <see cref="OperationCanceledException"/>: the engine calls it before each step,
/// after the previous step's result is saved, so the run stops between steps and stays saved for resume.
/// </summary>
public sealed class PipeRunProgress(
    MessageChannel channel, CancellationToken stopBeforeNextStep = default, Serilog.ILogger? log = null) : IRunProgress
{
    private readonly Lock _gate = new();
    private Task _tail = Task.CompletedTask;

    public void StepStarted(IStep step, int number, int total)
    {
        stopBeforeNextStep.ThrowIfCancellationRequested();
        Enqueue(new StepStarted(number, total, step.Id));
    }

    public void StepFinished(IStep step, StepResult result) =>
        Enqueue(new StepFinished(step.Id, result.Outcome, result.Messages, result.NextAction));

    /// <summary>Completes when every message queued so far has been sent (or failed to send).</summary>
    public Task DrainAsync()
    {
        lock (_gate) return _tail;
    }

    private void Enqueue(WorkerMessage message)
    {
        lock (_gate) _tail = SendAfterAsync(_tail, message);
    }

    private async Task SendAfterAsync(Task previous, WorkerMessage message)
    {
        await previous.ConfigureAwait(false);
        try
        {
            await channel.SendAsync(message, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The window is gone; the read loop sees the end of the stream and stops the run.
            log?.Warning("Worker channel: could not send {Type} ({Reason})", message.GetType().Name, ex.Message);
        }
    }
}
