using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeScheduledTasks : IScheduledTasks
{
    public int RegisterCalls { get; private set; }
    public int RemoveCalls { get; private set; }
    public Exception? RegisterException { get; set; }
    public Exception? RemoveException { get; set; }

    public Task RegisterResumeTaskAsync(CancellationToken ct)
    {
        RegisterCalls++;
        return RegisterException is null ? Task.CompletedTask : Task.FromException(RegisterException);
    }

    /// <summary>Whether a resume task exists for RemoveResumeTaskAsync to remove.</summary>
    public bool TaskPresent { get; set; } = true;

    public Task<bool> RemoveResumeTaskAsync(CancellationToken ct)
    {
        RemoveCalls++;
        return RemoveException is null ? Task.FromResult(TaskPresent) : Task.FromException<bool>(RemoveException);
    }
}
