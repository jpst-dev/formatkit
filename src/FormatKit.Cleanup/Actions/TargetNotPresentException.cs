namespace FormatKit.Cleanup.Actions;

/// <summary>Thrown by <see cref="IActionHandler.CaptureAsync"/> when the action's target does not exist on this system, so the item is skipped rather than failed.</summary>
internal sealed class TargetNotPresentException(string target)
    : Exception($"'{target}' is not present on this system.")
{
    public string Target { get; } = target;
}
