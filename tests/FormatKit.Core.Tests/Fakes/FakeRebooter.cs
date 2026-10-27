using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeRebooter : IRebooter
{
    public bool Result { get; set; } = true;
    public List<TimeSpan> Calls { get; } = [];

    public Task<bool> RebootAsync(TimeSpan countdown, CancellationToken ct)
    {
        Calls.Add(countdown);
        return Task.FromResult(Result);
    }
}
