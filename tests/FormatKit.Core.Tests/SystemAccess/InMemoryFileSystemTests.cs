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
}
