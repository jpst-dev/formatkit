using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class InMemoryFileSystemTests
{
    [Fact]
    public void InMemoryFileSystem_copy_move_readallbytes()
    {
        var files = new InMemoryFileSystem();
        files.Seed(@"C:\a\one.txt", "one");
        files.Seed(@"C:\a\two.txt", "two");

        Assert.Equal("one"u8.ToArray(), files.ReadAllBytes(@"C:\a\one.txt"));

        files.Copy(@"C:\a\one.txt", @"C:\a\two.txt");
        Assert.Equal("one", files.ReadAllText(@"C:\a\two.txt"));
        Assert.True(files.FileExists(@"C:\a\one.txt"));

        files.Move(@"C:\a\one.txt", @"C:\a\three.txt");
        Assert.False(files.FileExists(@"C:\a\one.txt"));
        Assert.Equal("one", files.ReadAllText(@"C:\a\three.txt"));

        Assert.Throws<IOException>(() => files.Move(@"C:\a\three.txt", @"C:\a\two.txt"));
    }

    [Fact]
    public void Enumeration_SkipsUnreadableFolderAndReportsItOnce()
    {
        var files = new InMemoryFileSystem();
        files.SeedEntry(@"C:\u\a.txt", 10);
        files.SeedEntry(@"C:\u\locked\b.txt", 20);
        files.SeedEntry(@"C:\u\locked\deep\c.txt", 30);
        files.MarkUnreadable(@"C:\u\locked");
        var unreadable = new List<string>();

        var entries = files.EnumerateFileEntries(@"C:\u", recursive: true, unreadable.Add).ToList();

        Assert.Equal([@"C:\u\a.txt"], entries.Select(e => e.Path));
        Assert.Equal([@"C:\u\locked"], unreadable);
    }

    [Fact]
    public void EnumerateFileEntries_YieldsSeededContentAndEntries()
    {
        var files = new InMemoryFileSystem();
        files.Seed(@"C:\u\text.txt", "h\u00e9");
        files.SeedEntry(@"C:\u\sub\big.bin", 1000, FileAttributes.Offline);

        var entries = files.EnumerateFileEntries(@"C:\u", recursive: true).OrderBy(e => e.Path).ToList();

        Assert.Equal(2, entries.Count);
        Assert.Equal(new FileEntry(@"C:\u\sub\big.bin", 1000, FileAttributes.Offline), entries[0]);
        Assert.Equal(new FileEntry(@"C:\u\text.txt", 3, FileAttributes.Normal), entries[1]);
        Assert.Single(files.EnumerateFileEntries(@"C:\u", recursive: false));
    }

    [Fact]
    public void EnumerateDirectories_ListsDirectChildrenAndSkipsHiddenSystemAndReparsePoints()
    {
        var files = new InMemoryFileSystem();
        files.SeedEntry(@"C:\u\a\f.txt", 1);
        files.SeedDirectory(@"C:\u\b", FileAttributes.Directory | FileAttributes.Hidden);
        files.SeedDirectory(@"C:\u\c", FileAttributes.Directory | FileAttributes.System);
        files.SeedDirectory(@"C:\u\link", FileAttributes.Directory | FileAttributes.ReparsePoint);
        files.SeedDirectory(@"C:\u\a\nested");

        Assert.Equal([@"C:\u\a"], files.EnumerateDirectories(@"C:\u"));
    }

    [Fact]
    public void EnumerateDirectories_Unreadable_CallsCallbackAndYieldsNothing()
    {
        var files = new InMemoryFileSystem();
        files.SeedEntry(@"C:\u\locked\a\f.txt", 1);
        files.MarkUnreadable(@"C:\u\locked");
        var unreadable = new List<string>();

        Assert.Empty(files.EnumerateDirectories(@"C:\u\locked", unreadable.Add));
        Assert.Equal([@"C:\u\locked"], unreadable);
    }
}
