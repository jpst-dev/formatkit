using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Checks;

/// <summary>
/// The item a check reports instead of Ok when a path is off the system drive but the volume list is unknown:
/// that drive could be a virtual disk stored on the system drive.
/// </summary>
internal static class UnknownLocation
{
    public static PrepareItem Item(IPrepareCheck check, PrepareContext context, string name, string path)
    {
        var drive = $"{char.ToUpperInvariant(context.Env.SystemDrive)}:";
        return new PrepareItem(check.Id, check.Group, PrepareStatus.NotChecked,
            Strings.Format("Location_Unknown_Title", name, drive),
            Strings.Format("Location_Unknown_Detail", path, drive),
            Strings.Format("Location_Unknown_Action", drive));
    }
}
