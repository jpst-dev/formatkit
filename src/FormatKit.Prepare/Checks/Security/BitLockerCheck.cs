using FormatKit.Core.SystemAccess;
using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Checks.Security;

/// <summary>Reports the BitLocker state of each fixed volume. Never prints a recovery key, only where to find it.</summary>
public sealed class BitLockerCheck : IPrepareCheck
{
    public const string CheckId = "security.bitlocker";

    public string Id => CheckId;

    public PrepareGroup Group => PrepareGroup.Security;

    public async Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
    {
        var volumes = await context.Services.Volumes.ListAsync(ct);
        var items = new List<PrepareItem>();
        foreach (var volume in volumes.OrderBy(v => v.Letter))
        {
            var letter = char.ToUpperInvariant(volume.Letter);
            if (volume.BitLocker == BitLockerState.Off)
                continue;

            if (volume.BitLocker == BitLockerState.Unknown)
            {
                items.Add(Item(PrepareStatus.NotChecked,
                    Strings.Format("BitLocker_Unknown_Title", letter),
                    Strings.Get("BitLocker_Unknown_Detail"),
                    Strings.Format("BitLocker_Unknown_Action", letter)));
                continue;
            }

            if (letter == context.Env.SystemDrive)
            {
                items.Add(Item(PrepareStatus.Ok,
                    Strings.Format("BitLocker_WindowsVolume_Title", letter),
                    Strings.Get("BitLocker_WindowsVolume_Detail")));
                continue;
            }

            var needsProtector = context.IsElevated && volume.HasRecoveryPassword == false;
            items.Add(Item(PrepareStatus.Attention,
                Strings.Format("BitLocker_OtherVolume_Title", letter),
                Strings.Format("BitLocker_OtherVolume_Detail", letter),
                Strings.Format(needsProtector ? "BitLocker_OtherVolumeNoProtector_Action" : "BitLocker_OtherVolume_Action", letter)));

            if (!context.IsElevated)
            {
                items.Add(Item(PrepareStatus.NotChecked,
                    Strings.Format("BitLocker_NotElevated_Title", letter),
                    Strings.Get("BitLocker_NotElevated_Detail"),
                    Strings.Format("BitLocker_NotElevated_Action", letter)));
            }
        }

        return items;
    }

    private PrepareItem Item(PrepareStatus status, string title, string? detail, string? action = null) =>
        new(Id, Group, status, title, detail, action);
}
