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
    /// (a 4 GB PC reads about 3.8 GiB), so the limit is 3.5 GiB to avoid warning about a 4 GB machine. The comparison is
    /// strict: exactly 3.5 GiB passes, one byte less needs attention.
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
            items.Add(Attention("Win11_Ram"));
        }

        if (info.Uefi == false)
        {
            items.Add(Attention("Win11_Uefi"));
        }
        else if (info.Uefi == true && info.SecureBoot == false)
        {
            items.Add(Attention("Win11_SecureBoot"));
        }

        if (info.TpmMajorVersion is { } tpm)
        {
            if (tpm < 2)
            {
                items.Add(Attention("Win11_Tpm"));
            }
        }
        else if (info.TpmAbsent)
        {
            items.Add(Attention("Win11_TpmNone"));
        }
        else
        {
            // Not elevated, the query failed or the version did not parse: never claim there is no TPM.
            items.Add(Item(PrepareStatus.NotChecked, context.IsElevated ? "Win11_TpmUnreadable" : "Win11_TpmNotElevated"));
        }

        var unknown = new List<string>();
        if (info.RamBytes is null)
        {
            unknown.Add(Strings.Get("Win11_Name_Ram"));
        }

        if (info.Uefi is null)
        {
            unknown.Add(Strings.Get("Win11_Name_Firmware"));
        }
        else if (info.Uefi == true && info.SecureBoot is null)
        {
            unknown.Add(Strings.Get("Win11_Name_SecureBoot"));
        }

        if (unknown.Count > 0)
        {
            items.Add(new PrepareItem(Id, Group, PrepareStatus.NotChecked, Strings.Get("Win11_Unknown_Title"),
                Strings.Format("Win11_Unknown_Detail", string.Join(", ", unknown)), WithProcessor("Win11_Unknown_Action")));
        }

        if (items.Count == 0)
        {
            // The Met texts name the processor themselves: FormatKit keeps no CPU list.
            items.Add(new PrepareItem(Id, Group, PrepareStatus.Ok, Strings.Get("Win11_Met_Title"), Strings.Get("Win11_Met_Detail"),
                Strings.Get("Win11_Met_Action")));
        }

        return items;
    }

    // Spec section 9: every Windows 10 result points at the PC Health Check app for the processor.
    private static string WithProcessor(string actionKey) => Strings.Get(actionKey) + " " + Strings.Get("Win11_Processor");

    private PrepareItem Attention(string prefix) =>
        new(Id, Group, PrepareStatus.Attention, Strings.Get(prefix + "_Title"),
            Strings.Get(prefix + "_Detail") + " " + Strings.Get("Win11_Consequence"), WithProcessor(prefix + "_Action"));

    private PrepareItem Item(PrepareStatus status, string prefix) =>
        new(Id, Group, status, Strings.Get(prefix + "_Title"), Strings.Get(prefix + "_Detail"), WithProcessor(prefix + "_Action"));
}
