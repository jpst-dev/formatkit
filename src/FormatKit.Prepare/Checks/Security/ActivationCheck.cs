using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Checks.Security;

/// <summary>Reports how Windows is licensed, so the user knows whether it reactivates after a reinstall. Never reads or prints a product key.</summary>
public sealed class ActivationCheck : IPrepareCheck
{
    public const string CheckId = "security.activation";

    public string Id => CheckId;

    public PrepareGroup Group => PrepareGroup.Security;

    public async Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
    {
        var activation = await context.Services.Licensing.GetWindowsActivationAsync(ct);
        return [activation switch
        {
            null => Item(PrepareStatus.NotChecked, "Activation_Unknown"),
            { Activated: false } => Item(PrepareStatus.Attention, "Activation_NotActivated"),
            { Channel: "OEM:DM" } => Item(PrepareStatus.Ok, "Activation_Firmware"),
            { Channel: { } channel } when channel.StartsWith("Volume", StringComparison.OrdinalIgnoreCase)
                => Item(PrepareStatus.Attention, "Activation_Organization"),
            _ => Item(PrepareStatus.Attention, "Activation_Retail"),
        }];
    }

    private PrepareItem Item(PrepareStatus status, string prefix) =>
        new(Id, Group, status, Strings.Get(prefix + "_Title"), Strings.Get(prefix + "_Detail"),
            status == PrepareStatus.Ok ? null : Strings.Get(prefix + "_Action"));
}
