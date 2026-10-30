namespace FormatKit.Core.SystemAccess;

/// <summary>
/// What the PC offers against the Windows 11 requirements. <c>Build</c> is the running Windows build; the other values are
/// null when they could not be read. <c>TpmMajorVersion</c> is null when the process is not elevated, the TPM query failed
/// or its version did not parse. <c>TpmAbsent</c> is true only when an elevated query succeeded and found no TPM.
/// </summary>
public sealed record ReadinessInfo(int Build, long? RamBytes, bool? Uefi, bool? SecureBoot, int? TpmMajorVersion, bool TpmAbsent = false);

/// <summary>Reads the facts needed to judge Windows 11 readiness. Throws <see cref="InvalidOperationException"/> on failure.</summary>
public interface IWindowsReadiness
{
    Task<ReadinessInfo> ReadAsync(CancellationToken ct);
}
