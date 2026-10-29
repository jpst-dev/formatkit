using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Checks.Security;

/// <summary>
/// On Windows 10, warns when the PC does not meet the Windows 11 requirements. The installation media does not bypass them.
/// Every result on Windows 10 points at the PC Health Check app for the authoritative answer.
/// </summary>
public sealed class Windows11RequirementsCheck : IPrepareCheck
{
    public const string CheckId = "security.windows11-requirements";

    /// <summary>First Windows 11 build.</summary>
    private const int FirstWindows11Build = 22000;

    /// <summary>
    /// Windows 11 asks for 4 GB. Win32_ComputerSystem reports the memory Windows can use, a little below what is installed
    /// (a 4 GB PC reads about 3.8 GiB), so the limit is 3.5 GiB to avoid warning about a 4 GB machine.
    /// </summary>
    private const long MinimumRamBytes = 3584L * 1024 * 1024;

    public string Id => CheckId;

    public PrepareGroup Group => PrepareGroup.Security;

    public async Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
    {
        var info = await context.Services.Readiness.ReadAsync(ct);
        if (info.Build >= FirstWindows11Build)
        {
            return [new PrepareItem(Id, Group, PrepareStatus.Ok, Strings.Get("Win11_Already_Title"), Strings.Get("Win11_Already_Detail"))];
        }

        var items = new List<PrepareItem>();
        if (info.RamBytes is { } ram && ram < MinimumRamBytes)
        {
            items.Add(Item(PrepareStatus.Attention, "Win11_Ram"));
        }

        if (info.Uefi == false)
        {
            items.Add(Item(PrepareStatus.Attention, "Win11_Uefi"));
        }
        else if (info.Uefi == true && info.SecureBoot == false)
        {
            items.Add(Item(PrepareStatus.Attention, "Win11_SecureBoot"));
        }

        if (info.TpmMajorVersion is { } tpm)
        {
            if (tpm < 2)
            {
                items.Add(Item(PrepareStatus.Attention, "Win11_Tpm"));
            }
        }
        else if (context.IsElevated)
        {
            items.Add(Item(PrepareStatus.Attention, "Win11_TpmNone"));
        }
        else
        {
            items.Add(Item(PrepareStatus.NotChecked, "Win11_TpmNotElevated"));
        }

        if (info.RamBytes is null || info.Uefi is null || (info.Uefi == true && info.SecureBoot is null))
        {
            items.Add(Item(PrepareStatus.NotChecked, "Win11_Unknown"));
        }

        if (items.Count == 0)
        {
            items.Add(Item(PrepareStatus.Ok, "Win11_Met"));
        }

        return items;
    }

    private PrepareItem Item(PrepareStatus status, string prefix) =>
        new(Id, Group, status, Strings.Get(prefix + "_Title"), Strings.Get(prefix + "_Detail"), Strings.Get(prefix + "_Action"));
}
