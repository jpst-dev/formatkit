using FormatKit.Core.SystemAccess;

namespace FormatKit.Media.Usb;

public enum UsbRefusal { None, NotUsb, BootOrSystem, HardDisk, HoldsSourceFiles, TooSmall }

/// <summary>A disk with the verdict of the stick rules; <see cref="SourcesOnDisk"/> lists the paths that sit on it.</summary>
public sealed record UsbCandidate(DiskInfo Disk, UsbRefusal Refusal, IReadOnlyList<string> SourcesOnDisk);

/// <summary>Rules 1-4 of spec section 5.2, applied in order; the first failing rule is the refusal.</summary>
public static class UsbEligibility
{
    public static UsbCandidate Check(DiskInfo disk, bool allowHardDisk, IReadOnlyList<string> sourcePaths, long requiredBytes)
    {
        if (!string.Equals(disk.BusType, "USB", StringComparison.OrdinalIgnoreCase))
        {
            return new(disk, UsbRefusal.NotUsb, []);
        }

        if (disk.IsBoot || disk.IsSystem)
        {
            return new(disk, UsbRefusal.BootOrSystem, []);
        }

        if (!disk.IsRemovable && !allowHardDisk)
        {
            return new(disk, UsbRefusal.HardDisk, []);
        }

        // Only a drive letter can match: a UNC path is never on the stick.
        var letters = disk.Volumes.Where(v => v.Letter is not null)
            .Select(v => char.ToUpperInvariant(v.Letter!.Value)).ToHashSet();
        var onDisk = sourcePaths
            .Where(p => p.Length >= 2 && p[1] == ':' && char.IsAsciiLetter(p[0]) && letters.Contains(char.ToUpperInvariant(p[0])))
            .ToList();
        if (onDisk.Count > 0)
        {
            return new(disk, UsbRefusal.HoldsSourceFiles, onDisk);
        }

        if (PowerShellDisks.UsableBytes(disk.SizeBytes) < requiredBytes)
        {
            return new(disk, UsbRefusal.TooSmall, []);
        }

        return new(disk, UsbRefusal.None, []);
    }
}
