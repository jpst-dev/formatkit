using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core.Steps;

namespace FormatKit.Cleanup.Actions;

internal interface IActionHandler
{
    /// <summary>Whether the system already matches the action's target state.</summary>
    Task<bool> IsAppliedAsync(CleanupAction action, StepContext ctx, CancellationToken ct);

    /// <summary>Reads the state the action is about to change, so it can be undone.</summary>
    Task<JournalAction> CaptureAsync(CleanupAction action, StepContext ctx, CancellationToken ct);

    /// <summary>Applies the action and re-reads to verify (throws <see cref="InvalidOperationException"/> on a mismatch). Returns the record to keep: <paramref name="captured"/>, possibly enriched.</summary>
    Task<JournalAction> ApplyAsync(CleanupAction action, JournalAction captured, StepContext ctx, CancellationToken ct);

    /// <summary>Restores the recorded previous state. Throws on failure.</summary>
    Task UndoAsync(JournalAction recorded, StepContext ctx, CancellationToken ct);
}
