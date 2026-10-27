namespace FormatKit.Core.SystemAccess;

public enum OptionalFeatureState
{
    Enabled,
    Disabled,
    Absent,
}

public interface IOptionalFeatures
{
    Task<OptionalFeatureState> GetStateAsync(string name, CancellationToken ct);

    /// <summary>Enables or disables a feature without restarting; returns whether a restart is needed.</summary>
    Task<bool> SetStateAsync(string name, bool enabled, CancellationToken ct);
}
