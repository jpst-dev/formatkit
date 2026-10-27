using FormatKit.Core.SystemAccess;

namespace FormatKit.Cli.Tests.Fakes;

public sealed class FakeElevation : IElevation
{
    public bool IsElevated { get; set; }

    /// <summary>Exit code returned by the relaunched child; null means the user declined UAC.</summary>
    public int? RelaunchResult { get; set; }

    public int RelaunchCalls { get; private set; }
    public List<string[]> RelaunchArgs { get; } = [];

    public Task<int?> RelaunchElevatedAsync(IReadOnlyList<string> args)
    {
        RelaunchCalls++;
        RelaunchArgs.Add([.. args]);
        return Task.FromResult(RelaunchResult);
    }
}
