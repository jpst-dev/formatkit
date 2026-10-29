using FormatKit.Core.Running;
using FormatKit.Core.Steps;

namespace FormatKit.App.Worker;

/// <summary>
/// Posts <see cref="StepStarted"/> and <see cref="StepFinished"/> to the window without blocking the engine; the
/// channel keeps them in order with later sends (the reboot countdown, the summary). When
/// <paramref name="stopBeforeNextStep"/> is cancelled, the next <see cref="StepStarted"/> throws
/// <see cref="OperationCanceledException"/>: the engine calls it before each step, after the previous step's result is
/// saved, so the run stops between steps and stays saved for resume.
/// </summary>
public sealed class PipeRunProgress(MessageChannel channel, CancellationToken stopBeforeNextStep = default) : IRunProgress
{
    public void StepStarted(IStep step, int number, int total)
    {
        stopBeforeNextStep.ThrowIfCancellationRequested();
        channel.Post(new StepStarted(number, total, step.Id));
    }

    public void StepFinished(IStep step, StepResult result) =>
        channel.Post(new StepFinished(step.Id, result.Outcome, result.Messages, result.NextAction));
}
