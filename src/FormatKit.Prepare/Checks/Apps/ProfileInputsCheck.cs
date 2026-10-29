using FormatKit.Core.Profiles;
using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Checks.Apps;

/// <summary>Checks every file or folder the profile points to (and the profile file itself), except the two the programs and drivers checks cover.</summary>
public sealed class ProfileInputsCheck : IPrepareCheck
{
    public const string CheckId = "apps.profile-inputs";

    private static readonly string[] CoveredElsewhere =
        ["modules.programs.importFile", "modules.drivers.networkDriverBackup"];

    public string Id => CheckId;

    public PrepareGroup Group => PrepareGroup.Apps;

    public Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
    {
        if (context.Profile is null)
            return Task.FromResult<IReadOnlyList<PrepareItem>>([]);

        var drive = ProfileSections.SystemDriveLabel(context);
        var items = new List<PrepareItem>();
        var fine = 0;
        foreach (var input in context.Inputs.Where(i => !CoveredElsewhere.Contains(i.JsonPath, StringComparer.Ordinal)))
        {
            ct.ThrowIfCancellationRequested();
            var name = input.JsonPath.Length == 0 ? Strings.Get("ProfileInputs_ProfileFile") : input.JsonPath;
            var location = context.Windows.Locate(input.ResolvedPath);
            if (location == PathLocation.Lost)
                items.Add(new PrepareItem(Id, Group, PrepareStatus.Blocker,
                    Strings.Format("ProfileInputs_Lost_Title", name, drive),
                    Strings.Format("ProfileInputs_Detail", input.ResolvedPath),
                    Strings.Format("ProfileInputs_Lost_Action", drive)));
            else if (!Exists(context, input))
                items.Add(new PrepareItem(Id, Group, PrepareStatus.Blocker,
                    Strings.Format("ProfileInputs_Missing_Title", name),
                    Strings.Format("ProfileInputs_Detail", input.ResolvedPath),
                    Strings.Get("ProfileInputs_Missing_Action")));
            else if (location == PathLocation.Unknown)
                items.Add(UnknownLocation.Item(this, context, name, input.ResolvedPath));
            else
                fine++;
        }

        if (items.Count == 0 && fine > 0)
            items.Add(new PrepareItem(Id, Group, PrepareStatus.Ok,
                fine == 1 ? Strings.Format("ProfileInputs_Ok_One", drive) : Strings.Format("ProfileInputs_Ok_Many", fine, drive)));
        return Task.FromResult<IReadOnlyList<PrepareItem>>(items);
    }

    private static bool Exists(PrepareContext context, ExternalInput input) =>
        input.Kind == ExternalInputKind.Folder
            ? context.Services.Files.DirectoryExists(input.ResolvedPath)
            : context.Services.Files.FileExists(input.ResolvedPath);
}
