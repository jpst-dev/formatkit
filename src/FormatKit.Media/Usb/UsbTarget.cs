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

    /// <summary>Lists again and erases only when the same disk (number and UniqueId) still passes every rule.</summary>
    public async Task<string> PrepareAsync(UsbCandidate confirmed, bool allowHardDisk, IReadOnlyList<string> sourcePaths,
        long requiredBytes, CancellationToken ct)
    {
        var number = confirmed.Disk.Number;
        var uniqueId = confirmed.Disk.UniqueId;
        if (string.IsNullOrWhiteSpace(uniqueId))
        {
            // The swap check below compares ids: a blank one would match any other blank disk.
            throw new DiskChangedException($"Disk {number} has no UniqueId to check against.");
        }

        var again = (await ListAsync(allowHardDisk, sourcePaths, requiredBytes, ct)).FirstOrDefault(c => c.Disk.Number == number);
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

        return await disks.PrepareUsbAsync(number, uniqueId, ct);
    }
}
