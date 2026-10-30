using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakePrinters(params string[] names) : IPrinters
{
    public List<string> Names { get; } = [.. names];

    /// <summary>When set, listing throws it.</summary>
    public Exception? Failure { get; set; }

    public int Calls { get; private set; }

    public Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct)
    {
        Calls++;
        return Failure is null ? Task.FromResult<IReadOnlyList<string>>(Names.ToList()) : Task.FromException<IReadOnlyList<string>>(Failure);
    }
}
