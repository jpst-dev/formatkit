using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Cleanup.Localization;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Actions;

internal sealed class AppxHandler : IActionHandler
{
    public const string Type = "appx";

    public async Task<bool> IsAppliedAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var state = await ctx.Services.Appx.GetStateAsync(((AppxAction)action).Package, ct).ConfigureAwait(false);
        return !state.InstalledForUser && !state.Provisioned;
    }

    public async Task<JournalAction> CaptureAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var a = (AppxAction)action;
        var state = await ctx.Services.Appx.GetStateAsync(a.Package, ct).ConfigureAwait(false);
        ctx.Log.Debug("Appx {Package} previous state: installed={Installed}, provisioned={Provisioned}", a.Package, state.InstalledForUser, state.Provisioned);
        var previous = JsonSerializer.SerializeToElement(new { package = a.Package, storeId = a.StoreId, provisioned = state.Provisioned });
        return new JournalAction(Type, a.Package, previous);
    }

    public async Task<JournalAction> ApplyAsync(CleanupAction action, JournalAction captured, StepContext ctx, CancellationToken ct)
    {
        var a = (AppxAction)action;
        await ctx.Services.Appx.RemoveAsync(a.Package, ct).ConfigureAwait(false);
        var state = await ctx.Services.Appx.GetStateAsync(a.Package, ct).ConfigureAwait(false);
        if (state.InstalledForUser || state.Provisioned)
        {
            throw new InvalidOperationException($"Appx package '{a.Package}' is still present after removal.");
        }

        ctx.Log.Information("Appx {Package} removed", a.Package);
        return captured;
    }

    public async Task UndoAsync(JournalAction recorded, StepContext ctx, CancellationToken ct)
    {
        var storeId = recorded.Previous.GetProperty("storeId").GetString()!;
        var result = await ctx.Services.Winget.InstallAsync(storeId, ct, "msstore").ConfigureAwait(false);
        switch (result.Status)
        {
            case WingetStatus.Installed:
            case WingetStatus.AlreadyInstalled:
                ctx.Log.Information("Appx {Package} reinstalled from the Store ({Status})", recorded.Target, result.Status);
                return;
            case WingetStatus.WingetMissing:
                throw new InvalidOperationException(Strings.Format("Undo_WingetMissing", storeId));
            default:
                throw new InvalidOperationException(
                    $"Could not reinstall '{recorded.Target}' (winget exit code {result.ExitCode}). Install it from the Store: ms-windows-store://pdp/?ProductId={storeId}");
        }
    }
}
