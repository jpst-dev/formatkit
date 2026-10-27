namespace FormatKit.Core.Steps;

public interface IStep
{
    string Id { get; }

    /// <summary>Already-localized title.</summary>
    string Title { get; }

    bool RequiresAdmin { get; }
    bool IsCritical { get; }

    /// <summary>Returns true when the step has work to do.</summary>
    Task<bool> CheckAsync(StepContext ctx, CancellationToken ct);

    Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct);
}
