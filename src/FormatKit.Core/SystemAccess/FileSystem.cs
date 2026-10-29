using System.Security;

namespace FormatKit.Core.SystemAccess;

public sealed class FileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void Copy(string source, string destination) => File.Copy(source, destination, overwrite: true);

    public void Move(string source, string destination) => File.Move(source, destination, overwrite: false);

    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

    public void WriteAllTextAtomic(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        File.Move(tmp, path, overwrite: true);
    }

    public void Delete(string path) => File.Delete(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    // Read access, read sharing only: while the stream is open nobody can write, replace or delete the file.
    public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    public IEnumerable<string> EnumerateFiles(string dir, string pattern, bool recursive) =>
        Directory.EnumerateFiles(dir, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

    public IEnumerable<string> EnumerateDirectories(string dir, Action<string>? onUnreadable = null)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        };
        List<string> children;
        try
        {
            children = new DirectoryInfo(dir).EnumerateDirectories("*", options).Select(d => d.FullName).ToList();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or SecurityException)
        {
            onUnreadable?.Invoke(dir);
            yield break;
        }

        foreach (var child in children)
            yield return child;
    }

    public IEnumerable<FileEntry> EnumerateFileEntries(string dir, bool recursive, Action<string>? onUnreadable = null)
    {
        // Hidden and system items are listed (only reparse-point folders are never entered).
        var options = new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0 };
        var pending = new Stack<string>();
        pending.Push(dir);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            List<FileEntry> files;
            List<string> subdirectories = [];
            try
            {
                var info = new DirectoryInfo(current);
                files = info.EnumerateFiles("*", options)
                    .Select(f => new FileEntry(f.FullName, f.Length, f.Attributes))
                    .ToList();
                if (recursive)
                {
                    subdirectories = info.EnumerateDirectories("*", options)
                        .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
                        .Select(d => d.FullName)
                        .ToList();
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or SecurityException)
            {
                onUnreadable?.Invoke(current);
                continue;
            }

            foreach (var file in files)
                yield return file;
            foreach (var sub in subdirectories)
                pending.Push(sub);
        }
    }
}
