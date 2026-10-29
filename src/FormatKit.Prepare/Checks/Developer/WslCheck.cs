using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Checks.Developer;

/// <summary>Lists WSL distributions, which live in VHDX files that a reinstall erases. Docker's internal distributions are skipped.</summary>
public sealed class WslCheck : IPrepareCheck
{
    public const string CheckId = "dev.wsl";

    private static readonly string[] Skipped = ["docker-desktop", "docker-desktop-data"];

    public string Id => CheckId;

    public PrepareGroup Group => PrepareGroup.Developer;

    public async Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
    {
        var wsl = context.Services.Wsl;
        if (!await wsl.IsAvailableAsync(ct))
            return [];

        var distros = await wsl.ListDistrosAsync(ct);
        return
        [
            .. distros
                .Where(d => !Skipped.Contains(d, StringComparer.OrdinalIgnoreCase))
                .Select(d => new PrepareItem(Id, Group, PrepareStatus.Attention,
                    Strings.Format("Wsl_Title", d), Strings.Format("Wsl_Detail", d), Strings.Format("Wsl_Action", d))),
        ];
    }
}
