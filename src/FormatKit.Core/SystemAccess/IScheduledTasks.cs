namespace FormatKit.Core.SystemAccess;

public interface IScheduledTasks
{
    Task RegisterResumeTaskAsync(CancellationToken ct);

    Task RemoveResumeTaskAsync(CancellationToken ct);
}
