namespace FormatKit.Core.SystemAccess;

/// <summary>Read-only view of the installed printers.</summary>
public interface IPrinters
{
    /// <summary>Names of all installed printers, virtual ones included. Throws when the list cannot be read.</summary>
    Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct);
}
