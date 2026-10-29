namespace FormatKit.Core.SystemAccess;

public sealed record VirtualDiskState(bool Attached, char? Letter, string? Label);

public sealed class VirtualDiskException(string message, Exception? inner = null) : Exception(message, inner);

public interface IVirtualDisks
{
    /// <summary>The state of the VHDX, or null when the file does not exist.</summary>
    Task<VirtualDiskState?> GetAsync(string vhdxPath, CancellationToken ct);

    Task AttachAsync(string vhdxPath, CancellationToken ct);

    /// <summary>Creates an expandable VHDX, attaches it and formats it as a Dev Drive. Cleans up the file on failure.</summary>
    Task CreateDevDriveAsync(string vhdxPath, int sizeGB, char letter, string label, CancellationToken ct);

    Task DetachAndDeleteAsync(string vhdxPath, CancellationToken ct);

    bool IsLetterInUse(char letter);

    long FreeBytes(string hostRoot);

    /// <summary>Registers (replacing any existing) a startup task that re-attaches the VHDX after a reboot.</summary>
    Task RegisterRemountTaskAsync(string vhdxPath, CancellationToken ct);
}
