using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Checks.Apps;

/// <summary>Checks that the exported program list the profile imports sits off the Windows drive, or asks for an export.</summary>
public sealed class ProgramsExportCheck : IPrepareCheck
{
    public const string CheckId = "apps.programs-export";

    private const string ImportFilePath = "modules.programs.importFile";

    public string Id => CheckId;

    public PrepareGroup Group => PrepareGroup.Apps;

    public Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
    {
        var input = ProfileSections.Input(context, ImportFilePath);
        if (input is null)
        {
            // Without a profile, or with an enabled section that has no importFile, the export is still to be done.
            return Task.FromResult<IReadOnlyList<PrepareItem>>(
                context.Profile is null || ProfileSections.IsEnabled(context, "programs") ? [Export()] : []);
        }

        var drive = ProfileSections.SystemDriveLabel(context);
        PrepareItem item;
        var location = context.Windows.Locate(input.ResolvedPath);
        if (location == PathLocation.Lost)
            item = new PrepareItem(Id, Group, PrepareStatus.Blocker,
                Strings.Format("ProgramsExport_Lost_Title", input.ResolvedPath, drive),
                Strings.Get("ProgramsExport_Lost_Detail"), Strings.Format("ProgramsExport_Lost_Action", drive));
        else if (!context.Services.Files.FileExists(input.ResolvedPath))
            item = new PrepareItem(Id, Group, PrepareStatus.Blocker,
                Strings.Format("ProgramsExport_Missing_Title", input.ResolvedPath),
                Strings.Get("ProgramsExport_Missing_Detail"), Strings.Get("ProgramsExport_Action"));
        else if (location == PathLocation.Unknown)
            item = UnknownLocation.Item(this, context, input.ResolvedPath, input.ResolvedPath);
        else
            item = new PrepareItem(Id, Group, PrepareStatus.Ok,
                Strings.Format("ProgramsExport_Ok_Title", input.ResolvedPath, drive));

        return Task.FromResult<IReadOnlyList<PrepareItem>>([item]);
    }

    private PrepareItem Export() =>
        new(Id, Group, PrepareStatus.Attention, Strings.Get("ProgramsExport_Todo_Title"),
            Strings.Get("ProgramsExport_Todo_Detail"), Strings.Get("ProgramsExport_Action"));
}
