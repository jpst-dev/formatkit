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
    void WriteAllTextAtomic(string path, string contents);
    void Delete(string path);
    void CreateDirectory(string path);
    /// <summary>Opens for reading; while open, others may read the file but not write, replace or delete it.</summary>
    Stream OpenRead(string path);
    IEnumerable<string> EnumerateFiles(string dir, string pattern, bool recursive);
}
