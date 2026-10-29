using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    private static readonly char[] Sep = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public int WriteCount { get; private set; }

    /// <summary>When true, every write throws <see cref="IOException"/>.</summary>
    public bool FailWrites { get; set; }

    public IReadOnlyDictionary<string, string> Files => _files;

    /// <summary>Seeds file content directly without counting as a write.</summary>
    public void Seed(string path, string contents)
    {
        _files[path] = contents;
        AddParents(path);
    }

    public bool FileExists(string path) => _files.ContainsKey(path);

    public bool DirectoryExists(string path) => _directories.Contains(path.TrimEnd(Sep));

    public string ReadAllText(string path) =>
        _files.TryGetValue(path, out var text) ? text : throw new FileNotFoundException(path);

    public void WriteAllTextAtomic(string path, string contents)
    {
        if (FailWrites)
            throw new IOException($"write refused: {path}");
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !DirectoryExists(dir))
            throw new DirectoryNotFoundException(dir);
        _files[path] = contents;
        WriteCount++;
    }

    /// <summary>When true, <see cref="Delete"/> throws <see cref="IOException"/>.</summary>
    public bool FailDeletes { get; set; }

    public void Delete(string path)
    {
        if (FailDeletes)
            throw new IOException($"delete refused: {path}");
        if (OpenHandles(path) > 0)
            throw new IOException($"file is open (no delete sharing): {path}");
        _files.Remove(path);
    }

    public void CreateDirectory(string path) => _directories.Add(path.TrimEnd(Sep));

    private readonly Dictionary<string, int> _open = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"open:path" and "close:path", in order, for every <see cref="OpenRead"/> stream.</summary>
    public List<string> Events { get; } = [];

    /// <summary>Read streams currently open on <paramref name="path"/>; like the real one, an open file cannot be deleted.</summary>
    public int OpenHandles(string path) => _open.GetValueOrDefault(path);

    public Stream OpenRead(string path)
    {
        if (!_files.TryGetValue(path, out var text))
            throw new FileNotFoundException(path);
        _open[path] = OpenHandles(path) + 1;
        Events.Add("open:" + path);
        return new TrackedStream(System.Text.Encoding.UTF8.GetBytes(text), () =>
        {
            _open[path]--;
            Events.Add("close:" + path);
        });
    }

    private sealed class TrackedStream(byte[] bytes, Action onClose) : MemoryStream(bytes, writable: false)
    {
        private bool _closed;

        protected override void Dispose(bool disposing)
        {
            if (!_closed)
            {
                _closed = true;
                onClose();
            }

            base.Dispose(disposing);
        }
    }

    public IEnumerable<string> EnumerateFiles(string dir, string pattern, bool recursive)
    {
        var prefix = dir.TrimEnd(Sep) + Path.DirectorySeparatorChar;
        return _files.Keys
            .Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Where(f => recursive || !f[prefix.Length..].Contains(Path.DirectorySeparatorChar))
            .Where(f => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(f), true))
            .ToList();
    }

    private void AddParents(string path)
    {
        var dir = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(dir))
        {
            _directories.Add(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }
}
