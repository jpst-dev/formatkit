using FormatKit.Core.Steps;

namespace FormatKit.Core.Running;

/// <summary>Receives step progress while a run executes, so the UI can show what is happening.</summary>
public interface IRunProgress
{
    /// <param name="number">1-based position of the step in the full plan.</param>
    void StepStarted(IStep step, int number, int total);

    void StepFinished(IStep step, StepResult result);
}
