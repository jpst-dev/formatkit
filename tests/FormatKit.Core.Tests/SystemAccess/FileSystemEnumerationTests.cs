using System.Diagnostics;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.SystemAccess;

public class FileSystemEnumerationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "formatkit-enum-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _junctions = [];

    public FileSystemEnumerationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // A non-recursive delete removes the link only, never its target.
        foreach (var junction in _junctions)
            Directory.Delete(junction, recursive: false);

        Directory.Delete(_dir, recursive: true);
    }

    private string Make(params string[] parts) => Path.Combine([_dir, .. parts]);

    private void CreateJunction(string link, string target)
    {
        Run("cmd.exe", ["/c", "mklink", "/J", link, target], "mklink /J failed");
        _junctions.Add(link);
    }

    private static void Run(string file, string[] args, string failure)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, failure);
    }

    [Fact]
    public void EnumerateDirectories_SkipsHiddenAndReparsePoints()
    {
        Directory.CreateDirectory(Make("a"));
        Directory.CreateDirectory(Make("b"));
        File.SetAttributes(Make("b"), FileAttributes.Directory | FileAttributes.Hidden);
        CreateJunction(Make("link"), Make("a"));

        var result = new FileSystem().EnumerateDirectories(_dir).ToList();

        Assert.Equal([Make("a")], result);
    }

    [Fact]
    public void EnumerateFileEntries_Recursive_ReportsLengthAndAttributes()
    {
        File.WriteAllBytes(Make("x.txt"), [1, 2, 3]);
        Directory.CreateDirectory(Make("sub"));
        File.WriteAllBytes(Make("sub", "y.bin"), [1, 2, 3, 4, 5]);
        File.SetAttributes(Make("sub", "y.bin"), FileAttributes.ReadOnly);

        var entries = new FileSystem().EnumerateFileEntries(_dir, recursive: true).ToList();

        Assert.Equal(2, entries.Count);
        Assert.Equal(3, entries.Single(e => e.Path == Make("x.txt")).Length);
        var y = entries.Single(e => e.Path == Make("sub", "y.bin"));
        Assert.Equal(5, y.Length);
        Assert.True(y.Attributes.HasFlag(FileAttributes.ReadOnly));

        File.SetAttributes(Make("sub", "y.bin"), FileAttributes.Normal);
    }

    [Fact]
    public void EnumerateFileEntries_NonRecursive_ListsOnlyDirectChildren()
    {
        File.WriteAllBytes(Make("x.txt"), [1]);
        Directory.CreateDirectory(Make("sub"));
        File.WriteAllBytes(Make("sub", "y.bin"), [1]);

        var entries = new FileSystem().EnumerateFileEntries(_dir, recursive: false).ToList();

        Assert.Equal([Make("x.txt")], entries.Select(e => e.Path));
    }

    [Fact]
    public void EnumerateFileEntries_DoesNotFollowJunctions()
    {
        Directory.CreateDirectory(Make("real"));
        File.WriteAllBytes(Make("real", "one.txt"), [1]);
        File.WriteAllBytes(Make("real", "two.txt"), [1]);
        CreateJunction(Make("link"), Make("real"));

        var entries = new FileSystem().EnumerateFileEntries(_dir, recursive: true).ToList();

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.StartsWith(Make("real") + Path.DirectorySeparatorChar, e.Path));
    }
}
