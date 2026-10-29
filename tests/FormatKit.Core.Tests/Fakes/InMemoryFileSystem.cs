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

    /// <summary>When set, <see cref="WriteAllTextAtomic"/> throws it.</summary>
    public Exception? AtomicWriteError { get; set; }

    public IReadOnlyDictionary<string, string> Files => _files;

    /// <summary>Seeds file content directly without counting as a write.</summary>
    public void Seed(string path, string contents)
    {
        _files[path] = contents;
        AddParents(path);
    }

    private readonly Dictionary<string, FileEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileAttributes> _directoryAttributes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Seeds a file known only by size and attributes (no content), for scans.</summary>
    public void SeedEntry(string path, long length, FileAttributes attributes = FileAttributes.Normal)
    {
        _entries[path] = new FileEntry(path, length, attributes);
        AddParents(path);
    }

    /// <summary>Seeds a folder (and its parents) with the given attributes.</summary>
    public void SeedDirectory(string path, FileAttributes attributes = FileAttributes.Directory)
    {
        var trimmed = path.TrimEnd(Sep);
        _directories.Add(trimmed);
        _directoryAttributes[trimmed] = attributes;
        AddParents(trimmed);
    }

    /// <summary>Makes listing <paramref name="dir"/> fail, as an access-denied folder would.</summary>
    public void MarkUnreadable(string dir) => _unreadable.Add(dir.TrimEnd(Sep));

    public bool FileExists(string path) => _files.ContainsKey(path);

    public bool DirectoryExists(string path) => _directories.Contains(path.TrimEnd(Sep));

    public string ReadAllText(string path) =>
        _files.TryGetValue(path, out var text) ? text : throw new FileNotFoundException(path);

    public byte[] ReadAllBytes(string path) => System.Text.Encoding.UTF8.GetBytes(ReadAllText(path));

    public void Copy(string source, string destination)
    {
        if (!_files.ContainsKey(source) && _entries.TryGetValue(source, out var entry))
            _entries[destination] = entry with { Path = destination };
        else
            _files[destination] = ReadAllText(source);
        AddParents(destination);
        WriteCount++;
    }

    public void Move(string source, string destination)
    {
        var text = ReadAllText(source);
        if (_files.ContainsKey(destination))
            throw new IOException($"destination exists: {destination}");
        if (OpenHandles(source) > 0)
            throw new IOException($"file is open (no delete sharing): {source}");
        _files.Remove(source);
        _files[destination] = text;
        AddParents(destination);
        WriteCount++;
    }

    /// <summary>The exact bytes of every <see cref="WriteAllBytes"/> call, by path.</summary>
    public Dictionary<string, byte[]> RawBytes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void WriteAllBytes(string path, byte[] bytes)
    {
        if (FailWrites)
            throw new IOException($"write refused: {path}");
        RawBytes[path] = bytes;
        _files[path] = System.Text.Encoding.UTF8.GetString(bytes);
        WriteCount++;
    }

    public void WriteAllTextAtomic(string path, string contents)
    {
        if (AtomicWriteError is not null)
            throw AtomicWriteError;
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

    public IEnumerable<string> EnumerateDirectories(string dir, Action<string>? onUnreadable = null)
    {
        var parent = dir.TrimEnd(Sep);
        if (_unreadable.Contains(parent))
        {
            onUnreadable?.Invoke(parent);
            return [];
        }

        const FileAttributes skipped = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint;
        return _directories
            .Where(d => IsDirectChild(parent, d))
            .Where(d => (AttributesOf(d) & skipped) == 0)
            .ToList();
    }

    public IEnumerable<FileEntry> EnumerateFileEntries(string dir, bool recursive, Action<string>? onUnreadable = null)
    {
        var result = new List<FileEntry>();
        var pending = new Stack<string>();
        pending.Push(dir.TrimEnd(Sep));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (_unreadable.Contains(current))
            {
                onUnreadable?.Invoke(current);
                continue;
            }

            result.AddRange(AllFileEntries().Where(f => IsDirectChild(current, f.Path)));
            if (recursive)
            {
                foreach (var sub in _directories.Where(d => IsDirectChild(current, d)))
                {
                    if ((AttributesOf(sub) & FileAttributes.ReparsePoint) == 0)
                        pending.Push(sub);
                }
            }
        }

        return result;
    }

    private IEnumerable<FileEntry> AllFileEntries() =>
        _files.Select(f => new FileEntry(f.Key, System.Text.Encoding.UTF8.GetByteCount(f.Value), FileAttributes.Normal))
            .Concat(_entries.Values);

    private FileAttributes AttributesOf(string directory) =>
        _directoryAttributes.GetValueOrDefault(directory, FileAttributes.Directory);

    private static bool IsDirectChild(string parent, string path) =>
        string.Equals(Path.GetDirectoryName(path)?.TrimEnd(Sep), parent, StringComparison.OrdinalIgnoreCase);

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
