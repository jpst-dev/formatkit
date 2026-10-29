namespace FormatKit.Core.SystemAccess;

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string ReadAllText(string path);
    byte[] ReadAllBytes(string path);
    /// <summary>Copies a file, overwriting the destination.</summary>
    void Copy(string source, string destination);
    /// <summary>Moves a file; throws <see cref="IOException"/> when the destination exists.</summary>
    void Move(string source, string destination);
    void WriteAllBytes(string path, byte[] bytes);
    void WriteAllTextAtomic(string path, string contents);
    void Delete(string path);
    void CreateDirectory(string path);
    /// <summary>Opens for reading; while open, others may read the file but not write, replace or delete it.</summary>
    Stream OpenRead(string path);
    IEnumerable<string> EnumerateFiles(string dir, string pattern, bool recursive);

    /// <summary>
    /// Direct child folders of <paramref name="dir"/>, skipping hidden, system and reparse-point folders.
    /// A folder that cannot be listed calls <paramref name="onUnreadable"/> and yields nothing.
    /// </summary>
    IEnumerable<string> EnumerateDirectories(string dir, Action<string>? onUnreadable = null);

    /// <summary>
    /// Files under <paramref name="dir"/> with length and attributes, never following reparse points.
    /// Each folder that cannot be listed calls <paramref name="onUnreadable"/> once and is skipped.
    /// </summary>
    IEnumerable<FileEntry> EnumerateFileEntries(string dir, bool recursive, Action<string>? onUnreadable = null);
}

/// <summary>A file seen by a read-only scan: full path, size in bytes and attributes.</summary>
public sealed record FileEntry(string Path, long Length, FileAttributes Attributes);
