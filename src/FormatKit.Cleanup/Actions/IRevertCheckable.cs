using FormatKit.Cleanup.Journal;
using FormatKit.Core.Steps;

namespace FormatKit.Cleanup.Actions;

/// <summary>
/// A handler whose journaled target can move or disappear without Windows reverting anything (a power plan switch, a
/// task removed by an update, an uninstalled startup program). <c>cleanup check</c> lists such an item as "not checked"
/// instead of "reverted by Windows".
/// </summary>
internal interface IRevertCheckable
{
    /// <summary>Whether the target <paramref name="recorded"/> changed is still the one the check reads. Read-only.</summary>
    Task<bool> CanCheckAsync(JournalAction recorded, StepContext ctx, CancellationToken ct);
}
