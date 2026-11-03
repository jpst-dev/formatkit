using FormatKit.Core.SystemAccess;

namespace FormatKit.Media.Usb;

public enum UsbRefusal { None, NotUsb, BootOrSystem, HardDisk, HoldsSourceFiles, TooSmall, NoUniqueId }

/// <summary>A disk with the verdict of the stick rules; <see cref="SourcesOnDisk"/> lists the paths that sit on it.</summary>
public sealed record UsbCandidate(DiskInfo Disk, UsbRefusal Refusal, IReadOnlyList<string> SourcesOnDisk);

/// <summary>
/// Rules 1-4 of spec section 5.2, applied in order, then a disk without a UniqueId; the first failing rule is the refusal.
/// </summary>
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
            .Where(p => DriveLetterOf(p) is { } l && letters.Contains(l))
            .ToList();
        if (onDisk.Count > 0)
        {
            return new(disk, UsbRefusal.HoldsSourceFiles, onDisk);
        }

        if (PowerShellDisks.UsableBytes(disk.SizeBytes) < requiredBytes)
        {
            return new(disk, UsbRefusal.TooSmall, []);
        }

        // Without a UniqueId the check after the confirmation cannot tell a swapped stick from the chosen one.
        if (string.IsNullOrWhiteSpace(disk.UniqueId))
        {
            return new(disk, UsbRefusal.NoUniqueId, []);
        }

        return new(disk, UsbRefusal.None, []);
    }

    /// <summary>
    /// The upper-case drive letter a path ends up on, or null for a UNC path. Extended prefixes are stripped and relative
    /// paths are resolved against the current directory, so no spelling of a path on the stick escapes rule 3.
    /// </summary>
    private static char? DriveLetterOf(string path)
    {
        var p = path;
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            p = p[4..];
            if (p.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        try
        {
            p = Path.GetFullPath(p);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Keep the raw string.
        }

        return p.Length >= 2 && p[1] == ':' && char.IsAsciiLetter(p[0]) ? char.ToUpperInvariant(p[0]) : null;
    }
}
