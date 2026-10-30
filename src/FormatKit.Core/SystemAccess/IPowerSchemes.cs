namespace FormatKit.Core.SystemAccess;

public interface IPowerSchemes
{
    Task<Guid> GetActiveAsync(CancellationToken ct);

    Task<IReadOnlyList<Guid>> ListAsync(CancellationToken ct);

    /// <summary>Duplicates a scheme and returns the new scheme's GUID.</summary>
    Task<Guid> DuplicateAsync(Guid source, CancellationToken ct);

    Task SetActiveAsync(Guid scheme, CancellationToken ct);

    Task DeleteAsync(Guid scheme, CancellationToken ct);

    /// <summary>The scheme's AC and DC index for a setting; null when the setting does not exist on this machine. <paramref name="subgroup"/> and <paramref name="setting"/> are GUIDs.</summary>
    Task<(int Ac, int Dc)?> GetValueAsync(Guid scheme, string subgroup, string setting, CancellationToken ct);

    /// <summary>Writes the AC index (and the DC index when <paramref name="dc"/> is set), then re-activates the scheme when it is the active one so the change takes effect.</summary>
    Task SetValueAsync(Guid scheme, string subgroup, string setting, int ac, int? dc, CancellationToken ct);
}

public static class BuiltInPowerSchemes
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    public static bool IsBuiltIn(Guid g) => g == Balanced || g == HighPerformance || g == Ultimate;
}
