using FormatKit.Core.SystemAccess;

namespace FormatKit.App.Tests.Fakes;

/// <summary>An elevation state the test chooses; relaunching is not used by the window.</summary>
public sealed class FakeAppElevation : IElevation
{
    public bool IsElevated { get; set; }

    public Task<int?> RelaunchElevatedAsync(IReadOnlyList<string> args) =>
        throw new InvalidOperationException("the window starts a worker instead of relaunching");
}
