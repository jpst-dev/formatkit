namespace FormatKit.Core.Steps;

public enum StepOutcome { Succeeded, Skipped, Failed, RebootRequired }

public sealed record StepResult(StepOutcome Outcome, IReadOnlyList<string> Messages, string? NextAction = null)
{
    public static StepResult Success(params string[] messages) => new(StepOutcome.Succeeded, messages);
    public static StepResult Skip(params string[] messages) => new(StepOutcome.Skipped, messages);
    public static StepResult Fail(string? nextAction, params string[] messages) => new(StepOutcome.Failed, messages, nextAction);
    public static StepResult Reboot(params string[] messages) => new(StepOutcome.RebootRequired, messages);
}
