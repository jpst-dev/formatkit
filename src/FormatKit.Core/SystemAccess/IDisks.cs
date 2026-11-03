namespace FormatKit.Core.SystemAccess;

/// <summary>A volume with a drive letter and/or label on a disk.</summary>
public sealed record DiskVolume(char? Letter, string? Label);

/// <summary>A physical disk. <see cref="BusType"/> is the invariant enum name, for example USB, NVMe or SATA.</summary>
public sealed record DiskInfo(
    int Number,
    string UniqueId,
    string FriendlyName,
    long SizeBytes,
    string BusType,
    bool IsRemovable,
    bool IsBoot,
    bool IsSystem,
    IReadOnlyList<DiskVolume> Volumes);

/// <summary>Thrown when the disk to erase is no longer the one that was chosen (another UniqueId, or gone).</summary>
public sealed class DiskChangedException(string message) : Exception(message);

/// <summary>Lists physical disks and prepares a USB stick.</summary>
public interface IDisks
{
    /// <summary>Lists the physical disks. Read-only. Throws <see cref="InvalidOperationException"/> on failure.</summary>
    Task<IReadOnlyList<DiskInfo>> ListAsync(CancellationToken ct);

    /// <summary>
    /// ERASES the disk, then creates one active FAT32 partition labeled FORMATKIT and returns its root such as <c>F:\</c>.
    /// Nothing is touched unless the disk still has <paramref name="uniqueId"/>; otherwise throws <see cref="DiskChangedException"/>.
    /// Other failures throw <see cref="InvalidOperationException"/>.
    /// </summary>
    Task<string> PrepareUsbAsync(int number, string uniqueId, CancellationToken ct);
}
