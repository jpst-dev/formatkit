using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeLicensing(WindowsActivation? value) : ILicensing
{
    public WindowsActivation? Value { get; set; } = value;

    /// <summary>When set, the read throws it.</summary>
    public Exception? Throw { get; set; }

    public Task<WindowsActivation?> GetWindowsActivationAsync(CancellationToken ct) =>
        Throw is null ? Task.FromResult(Value) : Task.FromException<WindowsActivation?>(Throw);
}
