namespace FormatKit.Core.SystemAccess;

public interface IRebooter
{
    /// <summary>Returns false when the user cancels the reboot.</summary>
    Task<bool> RebootAsync(TimeSpan countdown, CancellationToken ct);
}
