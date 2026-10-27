using FormatKit.Cli.Localization;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;

namespace FormatKit.Cli.Commands;

/// <summary>Prints a localized line as each step starts; the outcome is listed in the run summary.</summary>
internal sealed class ConsoleRunProgress(TextWriter output) : IRunProgress
{
    public void StepStarted(IStep step, int number, int total) =>
        output.WriteLine(Strings.Format("Run_StepStarted", number, total, step.Title));

    public void StepFinished(IStep step, StepResult result)
    {
    }
}
