using FormatKit.Core.Steps;

namespace FormatKit.Cli.Commands;

/// <summary>
/// Test-only wrapper (FORMATKIT_TEST_REBOOT_AFTER=&lt;step id&gt;): runs the step, then reports that a reboot
/// is required, so the reboot countdown, resume task and resume can be tested in a VM that has nothing
/// to install.
/// </summary>
internal sealed class SimulatedRebootStep(IStep inner) : IStep
{
    public const string EnvironmentVariable = "FORMATKIT_TEST_REBOOT_AFTER";

    public string Id => inner.Id;
    public string Title => inner.Title;
    public bool RequiresAdmin => inner.RequiresAdmin;
    public bool IsCritical => inner.IsCritical;

    public Task<bool> CheckAsync(StepContext ctx, CancellationToken ct) => Task.FromResult(false);

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var result = await inner.ExecuteAsync(ctx, ct).ConfigureAwait(false);
        return StepResult.Reboot([.. result.Messages, $"Simulated reboot ({EnvironmentVariable})."]);
    }

    public static IReadOnlyList<IStep> Apply(IReadOnlyList<IStep> plan, string? stepId) =>
        string.IsNullOrWhiteSpace(stepId)
            ? plan
            : [.. plan.Select(s => string.Equals(s.Id, stepId, StringComparison.OrdinalIgnoreCase) ? new SimulatedRebootStep(s) : s)];
}
