using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Checks.Developer;

/// <summary>Reports volumes backed by a VHDX on the Windows drive (a Dev Drive): the virtual disk is erased with it. Repositories inside are reported by <see cref="GitReposCheck"/>.</summary>
public sealed class DevDriveCheck : IPrepareCheck
{
    public const string CheckId = "dev.devdrive";

    public string Id => CheckId;

    public PrepareGroup Group => PrepareGroup.Developer;

    public Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
    {
        var items = new List<PrepareItem>();
        foreach (var volume in context.Windows.LostVhdxVolumes.OrderBy(v => v.Letter))
        {
            ct.ThrowIfCancellationRequested();
            var vhdx = volume.BackingVhdx!;
            var letter = $"{char.ToUpperInvariant(volume.Letter)}:";
            var size = SizeOf(context, vhdx);
            items.Add(new PrepareItem(Id, Group, PrepareStatus.Attention,
                Strings.Format("DevDrive_Title", letter, $"{char.ToUpperInvariant(context.Env.SystemDrive)}:"),
                size is null
                    ? Strings.Format("DevDrive_Detail", vhdx)
                    : Strings.Format("DevDrive_Detail_Size", vhdx, Sizes.Format(size.Value)),
                Strings.Format("DevDrive_Action", letter)));
        }

        if (!context.Windows.VolumesKnown)
        {
            var drive = $"{char.ToUpperInvariant(context.Env.SystemDrive)}:";
            items.Add(new PrepareItem(Id, Group, PrepareStatus.NotChecked,
                Strings.Format("DevDrive_Unlisted_Title", drive),
                Strings.Format("DevDrive_Unlisted_Detail", drive),
                Strings.Format("DevDrive_Unlisted_Action", drive)));
        }

        return Task.FromResult<IReadOnlyList<PrepareItem>>(items);
    }

    private static long? SizeOf(PrepareContext context, string vhdx)
    {
        var folder = Path.GetDirectoryName(vhdx);
        if (folder is null || !context.Services.Files.DirectoryExists(folder))
            return null;
        var entry = context.Services.Files.EnumerateFileEntries(folder, recursive: false)
            .FirstOrDefault(f => string.Equals(f.Path, vhdx, StringComparison.OrdinalIgnoreCase));
        return entry?.Length;
    }
}
