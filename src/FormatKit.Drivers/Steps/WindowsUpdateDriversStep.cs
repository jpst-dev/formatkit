using FormatKit.Core.Steps;
using FormatKit.Core.WindowsUpdate;
using FormatKit.Drivers.Localization;

namespace FormatKit.Drivers.Steps;

public sealed class WindowsUpdateDriversStep : IStep
{
    public string Id => "drivers.windows-update";
    public string Title => Strings.Get("Step_WindowsUpdateDrivers_Title");
    public bool RequiresAdmin => true;
    public bool IsCritical => false;

    public async Task<bool> CheckAsync(StepContext ctx, CancellationToken ct)
    {
        var updates = await ctx.Services.WindowsUpdate.SearchDriversAsync(ct);
        return updates.Count == 0;
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var updates = await ctx.Services.WindowsUpdate.SearchDriversAsync(ct);
        if (updates.Count == 0)
        {
            return StepResult.Skip();
        }

        var titles = string.Join(", ", updates.Select(u => u.Title));
        ctx.Log.Information("Installing {Count} driver update(s) from Windows Update", updates.Count);
        var result = await ctx.Services.WindowsUpdate.InstallAsync(updates, ct);

        if (!result.Succeeded)
        {
            ctx.Log.Warning("Windows Update driver install failed with HRESULT 0x{HResult:X8}", result.HResult);
            var failed = result.FailedTitles.Count > 0 ? string.Join(", ", result.FailedTitles) : titles;
            return StepResult.Fail(
                WindowsUpdateErrors.Describe(result.HResult),
                Strings.Format("Step_WindowsUpdateDrivers_Failed", failed));
        }

        return result.RebootRequired
            ? StepResult.Reboot(Strings.Format("Step_WindowsUpdateDrivers_RebootRequired", titles))
            : StepResult.Success(Strings.Format("Step_WindowsUpdateDrivers_Installed", titles));
    }
}
