namespace FormatKit.Core.SystemAccess;

/// <summary>Reads and toggles the enabled state of a scheduled task. Separate from <see cref="IScheduledTasks"/> (the resume task).</summary>
public interface IScheduledTaskControl
{
    /// <summary>Whether the task is enabled; null when the task does not exist.</summary>
    Task<bool?> IsEnabledAsync(string path, string name, CancellationToken ct);

    Task SetEnabledAsync(string path, string name, bool enabled, CancellationToken ct);
}
