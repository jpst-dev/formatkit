namespace FormatKit.Media.Usb;

/// <summary>The disks to show the user: only USB ones, without the external hard disks unless they are allowed.</summary>
public sealed record UsbListing(IReadOnlyList<UsbCandidate> Shown, int HiddenHardDisks)
{
    public static UsbListing Of(IReadOnlyList<UsbCandidate> candidates)
    {
        var usb = candidates.Where(c => string.Equals(c.Disk.BusType, "USB", StringComparison.OrdinalIgnoreCase)).ToList();
        return new UsbListing([.. usb.Where(c => c.Refusal != UsbRefusal.HardDisk)], usb.Count(c => c.Refusal == UsbRefusal.HardDisk));
    }
}
