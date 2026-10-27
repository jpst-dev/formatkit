using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Drivers.Catalog;
using FormatKit.Drivers.Localization;

namespace FormatKit.Drivers.Steps;

public sealed class OemToolStep(VendorCatalog catalog) : IStep
{
    public string Id => "drivers.oem-tool";
    public string Title => Strings.Get("Step_OemTool_Title");
    public bool RequiresAdmin => true;
    public bool IsCritical => false;

    public async Task<bool> CheckAsync(StepContext ctx, CancellationToken ct)
    {
        var hw = await ctx.Services.Hardware.ReadAsync(ct);
        return !MachineClassifier.Classify(hw, catalog).IsOem;
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var hw = await ctx.Services.Hardware.ReadAsync(ct);
        var tool = MachineClassifier.Classify(hw, catalog).Oem;
        if (tool is null)
        {
            return StepResult.Skip();
        }

        ctx.Log.Information("Installing OEM tool {Tool} ({WingetId})", tool.Id, tool.WingetId);
        var install = await ctx.Services.Winget.InstallAsync(tool.WingetId, ct);
        switch (install.Status)
        {
            case WingetStatus.WingetMissing:
                return StepResult.Fail(
                    Strings.Get("Step_OemTool_WingetMissingAction"),
                    Strings.Format("Step_OemTool_WingetMissing", tool.WingetId));
            case WingetStatus.Failed:
                ctx.Log.Warning("winget install {Id} failed with exit code {ExitCode}: {Output}", tool.WingetId, install.ExitCode, install.Output);
                return StepResult.Fail(
                    Strings.Format("Step_OemTool_InstallFailedAction", tool.WingetId),
                    Strings.Format("Step_OemTool_InstallFailed", tool.WingetId, install.ExitCode));
        }

        var executable = Environment.ExpandEnvironmentVariables(tool.Executable);
        ProcessResult run;
        try
        {
            run = await ctx.Services.Processes.RunAsync(new ProcessSpec(executable, tool.Arguments, Timeouts.Install), ct);
        }
        catch (FileNotFoundException)
        {
            return StepResult.Fail(
                Strings.Format("Step_OemTool_RunFailedAction", tool.WingetId),
                Strings.Format("Step_OemTool_NotFound", tool.WingetId, executable));
        }

        if (run.TimedOut)
        {
            return StepResult.Fail(
                Strings.Format("Step_OemTool_RunFailedAction", executable),
                Strings.Format("Step_OemTool_TimedOut", tool.WingetId));
        }

        if (tool.SuccessExitCodes.Contains(run.ExitCode))
        {
            return StepResult.Success(Strings.Format("Step_OemTool_Done", tool.WingetId));
        }

        if (tool.RebootExitCodes.Contains(run.ExitCode))
        {
            return StepResult.Reboot(Strings.Format("Step_OemTool_Reboot", tool.WingetId));
        }

        ctx.Log.Warning("{Executable} exited with {ExitCode}: {Output}", executable, run.ExitCode, run.StdOut + run.StdErr);
        return StepResult.Fail(
            Strings.Format("Step_OemTool_RunFailedAction", executable),
            Strings.Format("Step_OemTool_RunFailed", tool.WingetId, run.ExitCode));
    }
}
