using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Checks.Apps;

/// <summary>Checks that the network driver backup the profile uses is off the Windows drive and holds drivers, or asks for a backup.</summary>
public sealed class NetworkDriversCheck : IPrepareCheck
{
    public const string CheckId = "apps.network-drivers";

    private const string BackupPath = "modules.drivers.networkDriverBackup";

    public string Id => CheckId;

    public PrepareGroup Group => PrepareGroup.Apps;

    public Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
    {
        var input = ProfileSections.Input(context, BackupPath);
        if (input is null)
        {
            return Task.FromResult<IReadOnlyList<PrepareItem>>(
                context.Profile is null || ProfileSections.IsEnabled(context, "drivers") ? [Backup()] : []);
        }

        ct.ThrowIfCancellationRequested();
        var folder = input.ResolvedPath;
        var drive = ProfileSections.SystemDriveLabel(context);
        PrepareItem item;
        var location = context.Windows.Locate(folder);
        if (location == PathLocation.Lost)
        {
            item = new PrepareItem(Id, Group, PrepareStatus.Blocker,
                Strings.Format("NetworkDrivers_Lost_Title", folder, drive),
                Strings.Get("NetworkDrivers_Lost_Detail"), Strings.Format("NetworkDrivers_Lost_Action", drive, folder));
        }
        else
        {
            var (hasInf, unreadable) = FindInf(context, folder);
            if (hasInf && location == PathLocation.Unknown)
                item = UnknownLocation.Item(this, context, folder, folder);
            else if (hasInf)
                item = new PrepareItem(Id, Group, PrepareStatus.Ok, Strings.Format("NetworkDrivers_Ok_Title", folder, drive));
            else if (unreadable)
                item = new PrepareItem(Id, Group, PrepareStatus.NotChecked,
                    Strings.Format("NetworkDrivers_Unreadable_Title", folder),
                    Strings.Get("NetworkDrivers_Unreadable_Detail"), Strings.Format("NetworkDrivers_Empty_Action", folder));
            else
                item = new PrepareItem(Id, Group, PrepareStatus.Blocker,
                    Strings.Format("NetworkDrivers_Empty_Title", folder),
                    Strings.Get("NetworkDrivers_Empty_Detail"), Strings.Format("NetworkDrivers_Empty_Action", folder));
        }

        return Task.FromResult<IReadOnlyList<PrepareItem>>([item]);
    }

    private static (bool HasInf, bool Unreadable) FindInf(PrepareContext context, string folder)
    {
        var files = context.Services.Files;
        if (!files.DirectoryExists(folder))
            return (false, false);

        var unreadable = false;
        var hasInf = files.EnumerateFileEntries(folder, recursive: true, _ => unreadable = true)
            .Any(f => f.Path.EndsWith(".inf", StringComparison.OrdinalIgnoreCase));
        return (hasInf, unreadable);
    }

    private PrepareItem Backup() =>
        new(Id, Group, PrepareStatus.Attention, Strings.Get("NetworkDrivers_Todo_Title"),
            Strings.Get("NetworkDrivers_Todo_Detail"), Strings.Get("NetworkDrivers_Todo_Action"));
}
