namespace FormatKit.Core.SystemAccess;

public interface IScheduledTasks
{
    Task RegisterResumeTaskAsync(CancellationToken ct);

    /// <returns>True when a resume task existed and was removed; false when there was none.</returns>
    Task<bool> RemoveResumeTaskAsync(CancellationToken ct);
}
