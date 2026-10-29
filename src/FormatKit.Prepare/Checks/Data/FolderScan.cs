using FormatKit.Core.SystemAccess;

namespace FormatKit.Prepare.Checks.Data;

/// <summary>Size of the local files under a folder. Cloud-only placeholders are skipped; folders that could not be listed are counted.</summary>
internal sealed record FolderScan(long Bytes, int FileCount, int UnreadableFolders)
{
    private const FileAttributes CloudOnly = (FileAttributes)0x400000 | FileAttributes.Offline;

    /// <summary>Checks <paramref name="ct"/> per file, so Ctrl+C stops a large scan promptly.</summary>
    public static FolderScan Run(IFileSystem files, string folder, CancellationToken ct)
    {
        long bytes = 0;
        var count = 0;
        var unreadable = 0;
        foreach (var entry in files.EnumerateFileEntries(folder, recursive: true, _ => unreadable++))
        {
            ct.ThrowIfCancellationRequested();
            if ((entry.Attributes & CloudOnly) != 0)
                continue;
            bytes += entry.Length;
            count++;
        }

        return new FolderScan(bytes, count, unreadable);
    }
}
