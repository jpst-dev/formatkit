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

    public Task RemoveResumeTaskAsync(CancellationToken ct)
    {
        RemoveCalls++;
        return RemoveException is null ? Task.CompletedTask : Task.FromException(RemoveException);
    }
}
