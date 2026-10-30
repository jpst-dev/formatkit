using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeWindowsReadiness(ReadinessInfo? info = null) : IWindowsReadiness
{
    public ReadinessInfo Info { get; set; } = info ?? new ReadinessInfo(26100, 16L << 30, true, true, 2);

    /// <summary>When set, the read throws it.</summary>
    public Exception? Throw { get; set; }

    public Task<ReadinessInfo> ReadAsync(CancellationToken ct) =>
        Throw is null ? Task.FromResult(Info) : Task.FromException<ReadinessInfo>(Throw);
}
