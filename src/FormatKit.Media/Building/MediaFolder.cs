using System.Globalization;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Media.Building;

/// <summary>The free space of the drive a folder is on; <see cref="Error"/> holds why it could not be read.</summary>
public sealed record FolderSpace(string Root, long FreeBytes, string? Error);

/// <summary>The rules for the target folder of <c>media build</c>.</summary>
public static class MediaFolder
{
    private static readonly HashSet<string> RootSystemFolders =
        new(["System Volume Information", "$RECYCLE.BIN"], StringComparer.OrdinalIgnoreCase);

    public static bool IsEmptyOrMissing(IFileSystem files, string path)
    {
        if (files.FileExists(path))
            return false;
        if (!files.DirectoryExists(path))
            return true;

        // D1: a freshly formatted stick's root already holds these Windows folders; only at a drive root are they ignored.
        var full = Path.GetFullPath(path);
        var isRoot = string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase);
        bool Counts(string entry) => !isRoot || !RootSystemFolders.Contains(TopLevelName(full, entry));

        return !files.EnumerateFileEntries(path, recursive: true).Any(e => Counts(e.Path))
            && !files.EnumerateDirectories(path).Any(Counts);
    }

    public static FolderSpace ReadSpace(IVirtualDisks disks, string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? path;
        try
        {
            return new FolderSpace(root, disks.FreeBytes(root), null);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return new FolderSpace(root, 0, ex.Message);
        }
    }

    public static string FormatSize(long bytes) => bytes >= 1L << 30
        ? string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 30):0.0} GB")
        : string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 20):0} MB");

    private static string TopLevelName(string root, string entry) =>
        Path.GetRelativePath(root, entry).Split(Path.DirectorySeparatorChar, 2)[0];
}
