using System.Globalization;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Media.Usb;

/// <summary>Lists disks with their verdicts and, right before erasing, re-checks the confirmed disk.</summary>
public sealed class UsbTarget(IDisks disks)
{
    public async Task<IReadOnlyList<UsbCandidate>> ListAsync(bool allowHardDisk, IReadOnlyList<string> sourcePaths,
        long requiredBytes, CancellationToken ct)
    {
        var all = await disks.ListAsync(ct);
        return [.. all.Select(d => UsbEligibility.Check(d, allowHardDisk, sourcePaths, requiredBytes))];
    }

    /// <summary>
    /// The only way to erase a disk: asks for the typed number, which must be exactly the disk's number (spaces around
    /// it aside), then re-checks the disk (<see cref="PrepareAsync"/>). Returns the root, or null when the disk is
    /// refused, the answer is anything else or <paramref name="stopBeforeErase"/> fires before the erase starts; nothing
    /// is erased then. Throws <see cref="DiskChangedException"/>, also without erasing, when the re-check fails. The erase
    /// itself gets only <paramref name="ct"/>: an interrupted <c>diskpart</c> leaves the disk in an unknown state.
    /// </summary>
    public async Task<string?> ConfirmAndPrepareAsync(UsbCandidate disk, IDiskConfirmation confirmation, bool allowHardDisk,
        IReadOnlyList<string> sourcePaths, long requiredBytes, CancellationToken ct, CancellationToken stopBeforeErase = default)
    {
        if (disk.Refusal != UsbRefusal.None)
        {
            return null;
        }

        var answer = await confirmation.AskAsync(disk, ct);
        var expected = disk.Disk.Number.ToString(CultureInfo.InvariantCulture);
        if (answer is null || !string.Equals(answer.Trim(), expected, StringComparison.Ordinal))
        {
            return null;
        }

        return await PrepareAsync(disk, allowHardDisk, sourcePaths, requiredBytes, ct, stopBeforeErase);
    }

    /// <summary>
    /// Lists again and erases only when the same disk (number and UniqueId) still passes every rule. Returns null, with
    /// nothing erased, when <paramref name="stopBeforeErase"/> fires first.
    /// </summary>
    internal async Task<string?> PrepareAsync(UsbCandidate confirmed, bool allowHardDisk, IReadOnlyList<string> sourcePaths,
        long requiredBytes, CancellationToken ct, CancellationToken stopBeforeErase = default)
    {
        var number = confirmed.Disk.Number;
        var uniqueId = confirmed.Disk.UniqueId;
        if (string.IsNullOrWhiteSpace(uniqueId))
        {
            // The swap check below compares ids: a blank one would match any other blank disk.
            throw new DiskChangedException($"Disk {number} has no UniqueId to check against.");
        }

        using var listToken = CancellationTokenSource.CreateLinkedTokenSource(ct, stopBeforeErase);
        IReadOnlyList<UsbCandidate> candidates;
        try
        {
            candidates = await ListAsync(allowHardDisk, sourcePaths, requiredBytes, listToken.Token);
        }
        catch (OperationCanceledException) when (stopBeforeErase.IsCancellationRequested)
        {
            return null;
        }

        var again = candidates.FirstOrDefault(c => c.Disk.Number == number);
        if (again is null)
        {
            throw new DiskChangedException($"Disk {number} is no longer connected.");
        }

        if (again.Disk.UniqueId != uniqueId)
        {
            throw new DiskChangedException($"Disk {number} is not the disk that was confirmed.");
        }

        if (again.Refusal != UsbRefusal.None)
        {
            throw new DiskChangedException($"Disk {number} no longer passes the checks ({again.Refusal}).");
        }

        // The last check: the query took a while, and from here on the erase is not interrupted.
        if (stopBeforeErase.IsCancellationRequested)
        {
            return null;
        }

        return await disks.PrepareUsbAsync(number, uniqueId, ct);
    }
}
