using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeDisks(params DiskInfo[] disks) : IDisks
{
    public List<DiskInfo> Disks { get; } = [.. disks];

    /// <summary>The (number, uniqueId) pairs <see cref="PrepareUsbAsync"/> was called with.</summary>
    public List<(int Number, string UniqueId)> Prepared { get; } = [];

    /// <summary>The root <see cref="PrepareUsbAsync"/> returns.</summary>
    public string PrepareRoot { get; set; } = @"F:\";

    /// <summary>Runs just before the second <see cref="ListAsync"/> returns, so a test can change the list between calls.</summary>
    public Action? BeforeSecondList { get; set; }

    /// <summary>Runs when <see cref="PrepareUsbAsync"/> starts, so a test can act while the disk is being erased.</summary>
    public Action? DuringPrepare { get; set; }

    public Exception? ThrowOnPrepare { get; set; }

    /// <summary>How many times <see cref="ListAsync"/> was called.</summary>
    public int ListCalls { get; private set; }

    public Task<IReadOnlyList<DiskInfo>> ListAsync(CancellationToken ct)
    {
        ListCalls++;
        if (ListCalls == 2)
        {
            BeforeSecondList?.Invoke();
        }

        // As the real query, which is killed when its token is cancelled.
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<DiskInfo>>([.. Disks]);
    }

    public Task<string> PrepareUsbAsync(int number, string uniqueId, CancellationToken ct)
    {
        DuringPrepare?.Invoke();
        ct.ThrowIfCancellationRequested();
        if (ThrowOnPrepare is not null)
        {
            return Task.FromException<string>(ThrowOnPrepare);
        }

        Prepared.Add((number, uniqueId));
        return Task.FromResult(PrepareRoot);
    }
}
