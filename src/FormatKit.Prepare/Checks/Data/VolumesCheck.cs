using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Checks.Data;

/// <summary>Other volumes on the Windows disk are at risk in Setup; volumes on other disks are only informational.</summary>
public sealed class VolumesCheck : IPrepareCheck
{
    public const string CheckId = "data.volumes";

    public string Id => CheckId;

    public PrepareGroup Group => PrepareGroup.Data;

    public async Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
    {
        var lost = context.Windows.LostVhdxVolumes.Select(v => char.ToUpperInvariant(v.Letter)).ToHashSet();
        var volumes = (await context.Services.Volumes.ListAsync(ct))
            .Where(v => !lost.Contains(char.ToUpperInvariant(v.Letter)))
            .OrderBy(v => v.Letter)
            .ToList();

        var system = volumes.FirstOrDefault(v => char.ToUpperInvariant(v.Letter) == context.Env.SystemDrive);
        var others = volumes.Where(v => v != system).ToList();
        if (others.Count == 0)
            return [];

        if (system?.DiskNumber is null || others.Any(v => v.DiskNumber is null))
        {
            return [new PrepareItem(Id, Group, PrepareStatus.NotChecked,
                Strings.Get("Volumes_Unknown_Title"), Strings.Get("Volumes_Unknown_Detail"), Strings.Get("Volumes_Unknown_Action"))];
        }

        var items = new List<PrepareItem>();
        foreach (var volume in others)
        {
            var letter = char.ToUpperInvariant(volume.Letter);
            var disk = volume.DiskNumber ?? -1;
            items.Add(disk == system.DiskNumber
                ? new PrepareItem(Id, Group, PrepareStatus.Attention,
                    Strings.Format("Volumes_SameDisk_Title", letter), Strings.Format("Volumes_SameDisk_Detail", letter),
                    Strings.Format("Volumes_SameDisk_Action", letter))
                : new PrepareItem(Id, Group, PrepareStatus.Ok,
                    Strings.Format("Volumes_OtherDisk_Title", letter), Strings.Format("Volumes_OtherDisk_Detail", letter, disk)));
        }

        return items;
    }
}
