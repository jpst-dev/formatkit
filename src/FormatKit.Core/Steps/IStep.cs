namespace FormatKit.Core.Steps;

public interface IStep
{
    string Id { get; }

    /// <summary>Already-localized title.</summary>
    string Title { get; }

    bool RequiresAdmin { get; }
    bool IsCritical { get; }

    /// <summary>Returns true when the step's goal is already met (the step is then skipped).</summary>
    Task<bool> CheckAsync(StepContext ctx, CancellationToken ct);

    Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct);
}
