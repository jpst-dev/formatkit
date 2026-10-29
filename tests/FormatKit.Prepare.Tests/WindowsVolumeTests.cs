using FormatKit.Core.SystemAccess;

namespace FormatKit.Prepare.Tests;

public class WindowsVolumeTests
{
    private static VolumeInfo Vol(char letter, string? vhdx = null) => new(letter, 1, BitLockerState.Off, vhdx, null);

    [Fact]
    public void SystemDriveIsLostIgnoringCase() =>
        Assert.True(new WindowsVolume('C', []).IsLost("c:\\x"));

    [Fact]
    public void OtherDriveIsNotLost() =>
        Assert.False(new WindowsVolume('C', []).IsLost("D:\\x"));

    [Fact]
    public void UncPathIsNeverLost() =>
        Assert.False(new WindowsVolume('C', []).IsLost("\\\\srv\\share\\x"));

    [Fact]
    public void VolumeBackedByVhdxOnSystemDriveIsLost()
    {
        var windows = new WindowsVolume('C', [Vol('D', "C:\\DevDrive\\dev.vhdx")]);
        Assert.True(windows.IsLost("D:\\backup"));
        Assert.Equal('D', Assert.Single(windows.LostVhdxVolumes).Letter);
    }

    [Fact]
    public void VhdxOnAnotherDriveIsNotLost()
    {
        var windows = new WindowsVolume('C', [Vol('D', "E:\\dev.vhdx")]);
        Assert.False(windows.IsLost("D:\\backup"));
        Assert.Empty(windows.LostVhdxVolumes);
    }

    [Fact]
    public void RelativePathResolvesAgainstCurrentDirectory()
    {
        var current = Path.GetPathRoot(Directory.GetCurrentDirectory())![0];
        Assert.True(new WindowsVolume(current, []).IsLost("relative.txt"));
        Assert.False(new WindowsVolume(char.ToUpperInvariant(current) == 'Z' ? 'Y' : 'Z', []).IsLost("relative.txt"));
    }
}
