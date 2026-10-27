using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeOptionalFeatures : IOptionalFeatures
{
    public Dictionary<string, OptionalFeatureState> States { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Name, bool Enabled)> Changes { get; } = [];

    public bool RestartNeeded { get; set; }

    public Task<OptionalFeatureState> GetStateAsync(string name, CancellationToken ct) =>
        Task.FromResult(States.TryGetValue(name, out var state) ? state : OptionalFeatureState.Absent);

    public Task<bool> SetStateAsync(string name, bool enabled, CancellationToken ct)
    {
        States[name] = enabled ? OptionalFeatureState.Enabled : OptionalFeatureState.Disabled;
        Changes.Add((name, enabled));
        return Task.FromResult(RestartNeeded);
    }
}
