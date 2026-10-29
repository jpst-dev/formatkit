using FormatKit.Prepare.Checks.Apps;
using FormatKit.Prepare.Checks.Data;
using FormatKit.Prepare.Checks.Developer;
using FormatKit.Prepare.Checks.Security;

namespace FormatKit.Prepare;

/// <summary>Every check of the preparation checklist, in the order of the spec.</summary>
public static class PrepareChecks
{
    public static IReadOnlyList<IPrepareCheck> All() =>
    [
        new BitLockerCheck(),
        new ActivationCheck(),
        new UserFoldersCheck(),
        new OtherUsersCheck(),
        new VolumesCheck(),
        new GitReposCheck(),
        new WslCheck(),
        new KeysCheck(),
        new DevDriveCheck(),
        new ProgramsExportCheck(),
        new NetworkDriversCheck(),
        new ProfileInputsCheck(),
        new BrowsersCheck(),
    ];
}
