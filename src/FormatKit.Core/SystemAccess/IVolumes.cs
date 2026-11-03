namespace FormatKit.Core.SystemAccess;

public enum BitLockerState { Off, On, Encrypting, Decrypting, Suspended, Locked, Unknown }

/// <param name="Letter">Upper-case drive letter.</param>
/// <param name="DiskNumber">Null when the disk cannot be resolved.</param>
/// <param name="BackingVhdx">Path of the VHDX file when the volume lives on a file-backed virtual disk.</param>
/// <param name="HasRecoveryPassword">Null when unknown (not elevated, or the query failed).</param>
public sealed record VolumeInfo(char Letter, int? DiskNumber, BitLockerState BitLocker, string? BackingVhdx, bool? HasRecoveryPassword)
{
    /// <summary>The volume may be on a virtual disk, but where its file is could not be read (see <see cref="PowerShellVolumes"/>).</summary>
    public bool BackingVhdxUnknown { get; init; }
}

/// <summary>Read-only view of the fixed volumes that have a drive letter.</summary>
public interface IVolumes
{
    Task<IReadOnlyList<VolumeInfo>> ListAsync(CancellationToken ct);
}
