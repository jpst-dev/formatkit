namespace FormatKit.Core.SystemAccess;

public interface IPowerSchemes
{
    Task<Guid> GetActiveAsync(CancellationToken ct);

    Task<IReadOnlyList<Guid>> ListAsync(CancellationToken ct);

    /// <summary>Duplicates a scheme and returns the new scheme's GUID.</summary>
    Task<Guid> DuplicateAsync(Guid source, CancellationToken ct);

    Task SetActiveAsync(Guid scheme, CancellationToken ct);

    Task DeleteAsync(Guid scheme, CancellationToken ct);
}

public static class BuiltInPowerSchemes
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    public static bool IsBuiltIn(Guid g) => g == Balanced || g == HighPerformance || g == Ultimate;
}
