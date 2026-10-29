using FormatKit.Core.SystemAccess;

namespace FormatKit.Prepare.Tests;

public class WindowsVolumeTests
{
    private static VolumeInfo Vol(char letter, string? vhdx = null) => new(letter, 1, BitLockerState.Off, vhdx, null);

    /// <summary>A volume list that includes the system drive C, so the map is known.</summary>
    private static WindowsVolume Known(params VolumeInfo[] others) => new('C', [Vol('C'), .. others]);

    [Fact]
    public void SystemDriveIsLostIgnoringCase() =>
        Assert.True(Known().IsLost("c:\\x"));

    [Fact]
    public void OtherDriveIsNotLost()
    {
        Assert.False(Known().IsLost("D:\\x"));
        Assert.Equal(PathLocation.Kept, Known().Locate("D:\\x"));
    }

    [Fact]
    public void UncPathIsNeverLost()
    {
        Assert.False(Known().IsLost("\\\\srv\\share\\x"));
        Assert.Equal(PathLocation.Kept, new WindowsVolume('C', null).Locate("\\\\srv\\share\\x"));
    }

    [Fact]
    public void VolumeBackedByVhdxOnSystemDriveIsLost()
    {
        var windows = Known(Vol('D', "C:\\DevDrive\\dev.vhdx"));
        Assert.True(windows.IsLost("D:\\backup"));
        Assert.Equal('D', Assert.Single(windows.LostVhdxVolumes).Letter);
    }

    [Fact]
    public void VhdxOnAnotherDriveIsNotLost()
    {
        var windows = Known(Vol('D', "E:\\dev.vhdx"));
        Assert.False(windows.IsLost("D:\\backup"));
        Assert.Empty(windows.LostVhdxVolumes);
    }

    [Fact]
    public void RelativePathResolvesAgainstCurrentDirectory()
    {
        var current = Path.GetPathRoot(Directory.GetCurrentDirectory())![0];
        Assert.True(new WindowsVolume(current, []).IsLost("relative.txt"));
        var other = char.ToUpperInvariant(current) == 'Z' ? 'Y' : 'Z';
        Assert.False(new WindowsVolume(other, [Vol(other)]).IsLost("relative.txt"));
    }

    [Fact]
    public void KnownOnlyWhenTheListHasTheSystemDrive()
    {
        Assert.True(Known().VolumesKnown);
        Assert.False(new WindowsVolume('C', null).VolumesKnown);
        Assert.False(new WindowsVolume('C', []).VolumesKnown);
        Assert.False(new WindowsVolume('C', [Vol('D')]).VolumesKnown);
        Assert.Empty(new WindowsVolume('C', null).Volumes);
        Assert.Equal(['C', 'D'], Known(Vol('D')).Volumes.Select(v => v.Letter));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void AnotherDriveIsUnknownWhenTheVolumesAreNotKnown(bool? listWithoutSystemDrive)
    {
        // null: listing failed; false: empty list; true: list without C.
        IReadOnlyList<VolumeInfo>? volumes = listWithoutSystemDrive switch
        {
            null => null,
            false => [],
            true => [Vol('E')],
        };
        var windows = new WindowsVolume('C', volumes);

        Assert.Equal(PathLocation.Unknown, windows.Locate("D:\\backup"));
        Assert.False(windows.IsLost("D:\\backup"));
        Assert.True(windows.MayBeLost("D:\\backup"));
        Assert.Equal(PathLocation.Lost, windows.Locate("C:\\x"));
    }

    [Theory]
    [InlineData("\\\\?\\C:\\Users\\joao\\backup")]
    [InlineData("\\\\.\\C:\\Users\\joao\\backup")]
    [InlineData("\\\\?\\c:\\x")]
    public void LocalDevicePathsOnTheSystemDriveAreLost(string path) =>
        Assert.True(Known().IsLost(path));

    [Fact]
    public void LocalDevicePathOnALostVhdxIsLost() =>
        Assert.True(Known(Vol('D', "C:\\dev.vhdx")).IsLost("\\\\?\\D:\\code"));

    [Fact]
    public void LocalDevicePathOnAnotherDriveIsKept() =>
        Assert.Equal(PathLocation.Kept, Known().Locate("\\\\?\\D:\\backup"));

    [Fact]
    public void LongUncPathIsNeverLost() =>
        Assert.Equal(PathLocation.Kept, Known().Locate("\\\\?\\UNC\\srv\\share\\x"));
}
