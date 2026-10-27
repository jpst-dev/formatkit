using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Drivers.Catalog;
using FormatKit.Drivers.Localization;

namespace FormatKit.Drivers.Steps;

public sealed class GpuStep(VendorCatalog catalog) : IStep
{
    public string Id => "drivers.gpu";
    public string Title => Strings.Get("Step_Gpu_Title");
    public bool RequiresAdmin => true;
    public bool IsCritical => false;

    public async Task<bool> CheckAsync(StepContext ctx, CancellationToken ct)
    {
        foreach (var package in await GetPackagesAsync(ctx, ct))
        {
            if (!await ctx.Services.Winget.IsInstalledAsync(package.WingetId, ct, package.Source))
            {
                return false;
            }
        }

        return true;
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var installed = new List<string>();
        var failed = new List<string>();
        var wingetMissing = false;

        foreach (var package in await GetPackagesAsync(ctx, ct))
        {
            if (await ctx.Services.Winget.IsInstalledAsync(package.WingetId, ct, package.Source))
            {
                continue;
            }

            ctx.Log.Information("Installing GPU package {WingetId}", package.WingetId);
            var result = await ctx.Services.Winget.InstallAsync(package.WingetId, ct, package.Source);
            switch (result.Status)
            {
                case WingetStatus.Installed or WingetStatus.AlreadyInstalled:
                    installed.Add(package.WingetId);
                    break;
                case WingetStatus.WingetMissing:
                    wingetMissing = true;
                    failed.Add(package.WingetId);
                    break;
                default:
                    ctx.Log.Warning("winget install {Id} failed with exit code {ExitCode}: {Output}", package.WingetId, result.ExitCode, result.Output);
                    failed.Add(package.WingetId);
                    break;
            }
        }

        if (failed.Count > 0)
        {
            return StepResult.Fail(
                Strings.Get(wingetMissing ? "Step_Gpu_WingetMissingAction" : "Step_Gpu_FailedAction"),
                Strings.Format("Step_Gpu_Failed", string.Join(", ", failed)));
        }

        return installed.Count == 0
            ? StepResult.Skip()
            : StepResult.Success([.. installed.Select(id => Strings.Format("Step_Gpu_Installed", id))]);
    }

    private async Task<List<GpuPackage>> GetPackagesAsync(StepContext ctx, CancellationToken ct)
    {
        var hw = await ctx.Services.Hardware.ReadAsync(ct);
        return hw.Gpus
            .Select(g => catalog.Gpu.FirstOrDefault(p => p.Vendor == g.Vendor))
            .OfType<GpuPackage>()
            .DistinctBy(p => p.WingetId)
            .ToList();
    }
}
