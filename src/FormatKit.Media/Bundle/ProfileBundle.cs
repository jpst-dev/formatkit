using System.Text.Json;
using System.Text.Json.Nodes;
using FormatKit.Core.Profiles;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Media.Bundle;

public sealed record BundleItem(ExternalInput Input, string RelativePath, long Bytes);

/// <summary>The profile and its input files as they go to the media's FormatKit folder, with relative paths.</summary>
public sealed class ProfileBundle
{
    private readonly LoadedProfile _profile;
    // JSON path -> relative path, for every input that exists (including repeats of the same file).
    private readonly List<(string JsonPath, string RelativePath)> _rewrites;
    private readonly long _profileBytes;

    private ProfileBundle(LoadedProfile profile, List<BundleItem> items, List<(string, string)> rewrites,
        List<string> missing, long profileBytes)
    {
        _profile = profile;
        Items = items;
        _rewrites = rewrites;
        Missing = missing;
        _profileBytes = profileBytes;
    }

    public IReadOnlyList<BundleItem> Items { get; }
    public IReadOnlyList<string> Missing { get; }
    public long TotalBytes => Items.Sum(i => i.Bytes) + _profileBytes;

    public static ProfileBundle Plan(LoadedProfile profile, IReadOnlyList<ExternalInput> inputs, IFileSystem files)
    {
        var items = new List<BundleItem>();
        var rewrites = new List<(string, string)>();
        var missing = new List<string>();
        var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in inputs)
        {
            var key = input.ResolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (byPath.TryGetValue(key, out var existing))
            {
                rewrites.Add((input.JsonPath, existing));
                continue;
            }

            long bytes;
            if (input.Kind == ExternalInputKind.Folder)
            {
                if (!files.DirectoryExists(key)) { missing.Add(input.JsonPath); continue; }
                bytes = files.EnumerateFileEntries(key, recursive: true).Sum(e => e.Length);
            }
            else
            {
                if (!files.FileExists(key)) { missing.Add(input.JsonPath); continue; }
                bytes = FileLength(key, files);
            }

            var relative = Path.Combine("inputs", $"{items.Count + 1}-{Path.GetFileName(key)}");
            byPath[key] = relative;
            items.Add(new BundleItem(input with { ResolvedPath = key }, relative, bytes));
            rewrites.Add((input.JsonPath, relative));
        }

        return new ProfileBundle(profile, items, rewrites, missing, FileLength(profile.Path, files));
    }

    public void Write(string formatKitFolder, string exePath, IFileSystem files, IProgress<long>? copiedBytes,
        CancellationToken ct)
    {
        if (Missing.Count > 0)
            throw new InvalidOperationException("Inputs are missing: " + string.Join(", ", Missing));

        long copied = 0;
        files.CreateDirectory(formatKitFolder);

        ct.ThrowIfCancellationRequested();
        files.Copy(exePath, Path.Combine(formatKitFolder, "formatkit.exe"));

        foreach (var item in Items)
        {
            var target = Path.Combine(formatKitFolder, item.RelativePath);
            if (item.Input.Kind == ExternalInputKind.File)
            {
                ct.ThrowIfCancellationRequested();
                files.CreateDirectory(Path.GetDirectoryName(target)!);
                files.Copy(item.Input.ResolvedPath, target);
                copiedBytes?.Report(copied += item.Bytes);
                continue;
            }

            files.CreateDirectory(target);
            var root = item.Input.ResolvedPath;
            foreach (var entry in files.EnumerateFileEntries(root, recursive: true))
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, entry.Path);
                var dest = Path.Combine(target, relative);
                files.CreateDirectory(Path.GetDirectoryName(dest)!);
                files.Copy(entry.Path, dest);
                copiedBytes?.Report(copied += entry.Length);
            }
        }

        ct.ThrowIfCancellationRequested();
        var node = JsonNode.Parse(files.ReadAllText(_profile.Path), null, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) ?? throw new InvalidOperationException("Profile is empty.");
        foreach (var (jsonPath, relative) in _rewrites)
            JsonPathWriter.Set(node, jsonPath, relative);
        files.WriteAllTextAtomic(Path.Combine(formatKitFolder, "profile.json"),
            node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static long FileLength(string path, IFileSystem files)
    {
        var dir = Path.GetDirectoryName(path)!;
        foreach (var e in files.EnumerateFileEntries(dir, recursive: false))
            if (string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))
                return e.Length;
        return 0;
    }
}
