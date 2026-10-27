using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.SystemAccess;

public class FileSystemTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "formatkit-fs-" + Guid.NewGuid().ToString("N"));

    public FileSystemTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void WriteAllTextAtomic_replaces_existing_file_and_leaves_no_tmp()
    {
        var fs = new FileSystem();
        var path = Path.Combine(_dir, "state.json");
        File.WriteAllText(path, "old");

        fs.WriteAllTextAtomic(path, "new");

        Assert.Equal("new", fs.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Single(Directory.GetFiles(_dir));
    }
}
