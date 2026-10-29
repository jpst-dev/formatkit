namespace FormatKit.Core.SystemAccess;

/// <param name="Channel">Product key channel id (for example "Retail" or "OEM:DM"); never a key.</param>
public sealed record WindowsActivation(bool Activated, string? Channel);

public interface ILicensing
{
    /// <summary>Null when no Windows product with a key is found.</summary>
    Task<WindowsActivation?> GetWindowsActivationAsync(CancellationToken ct);
}
