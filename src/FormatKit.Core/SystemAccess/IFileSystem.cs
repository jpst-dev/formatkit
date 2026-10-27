namespace FormatKit.Core.SystemAccess;

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string ReadAllText(string path);
    void WriteAllTextAtomic(string path, string contents);
    void Delete(string path);
    void CreateDirectory(string path);
    IEnumerable<string> EnumerateFiles(string dir, string pattern, bool recursive);
}
