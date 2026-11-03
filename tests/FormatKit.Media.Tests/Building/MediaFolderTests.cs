using FormatKit.Core.Tests.Fakes;
using FormatKit.Media.Building;

namespace FormatKit.Media.Tests.Building;

public class MediaFolderTests
{
    private readonly InMemoryFileSystem _files = new();

    [Fact]
    public void Missing_folder_counts_as_empty() => Assert.True(MediaFolder.IsEmptyOrMissing(_files, @"C:\out"));

    [Fact]
    public void Folder_with_a_file_is_refused()
    {
        _files.Seed(@"C:\out\a.txt", "x");

        Assert.False(MediaFolder.IsEmptyOrMissing(_files, @"C:\out"));
    }

    [Fact]
    public void System_folders_count_only_outside_a_drive_root()
    {
        _files.Seed(@"F:\System Volume Information\x", "x");
        _files.Seed(@"C:\out\System Volume Information\x", "x");

        Assert.True(MediaFolder.IsEmptyOrMissing(_files, @"F:\"));
        Assert.False(MediaFolder.IsEmptyOrMissing(_files, @"C:\out"));
    }

    [Fact]
    public void ReadSpace_reads_the_drive_of_the_folder()
    {
        var disks = new FakeVirtualDisks();

        var space = MediaFolder.ReadSpace(disks, @"F:\out\media");

        Assert.Equal(@"F:\", space.Root);
        Assert.Null(space.Error);
        Assert.Equal(disks.FreeBytes(@"F:\"), space.FreeBytes);
    }

    [Theory]
    [InlineData(1L << 30, "1.0 GB")]
    [InlineData(5L << 29, "2.5 GB")]
    [InlineData(1L << 20, "1 MB")]
    public void Size_is_written_in_gb_or_mb(long bytes, string expected)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, MediaFolder.FormatSize(bytes));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }
}
