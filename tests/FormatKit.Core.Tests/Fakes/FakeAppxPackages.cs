using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeAppxPackages : IAppxPackages
{
    public Dictionary<string, AppxState> States { get; } = new(StringComparer.Ordinal);

    public List<string> Removed { get; } = [];

    public bool ThrowOnRemove { get; set; }

    public Task<AppxState> GetStateAsync(string packageName, CancellationToken ct) =>
        Task.FromResult(States.TryGetValue(packageName, out var state) ? state : new AppxState(false, false));

    public Task RemoveAsync(string packageName, CancellationToken ct)
    {
        if (ThrowOnRemove)
        {
            throw new InvalidOperationException($"Remove failed for '{packageName}'.");
        }

        States[packageName] = new AppxState(false, false);
        Removed.Add(packageName);
        return Task.CompletedTask;
    }
}
