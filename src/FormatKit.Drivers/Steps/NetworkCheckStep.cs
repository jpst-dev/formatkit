using FormatKit.Core.Steps;
using FormatKit.Drivers.Localization;

namespace FormatKit.Drivers.Steps;

public sealed class NetworkCheckStep(string? backupDirectory, TimeSpan retryDelay) : IStep
{
    private const int ProbeAttempts = 3;

    public string Id => "drivers.network-check";
    public string Title => Strings.Get("Step_NetworkCheck_Title");
    public bool RequiresAdmin => true;
    public bool IsCritical => true;

    public Task<bool> CheckAsync(StepContext ctx, CancellationToken ct) =>
        ctx.Services.Network.IsOnlineAsync(ct);

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory))
        {
            return StepResult.Fail(
                Strings.Get("Step_NetworkCheck_NoBackupAction"),
                Strings.Get("Step_NetworkCheck_NoBackup"));
        }

        if (!ctx.Services.Files.DirectoryExists(backupDirectory))
        {
            return StepResult.Fail(
                Strings.Format("Step_NetworkCheck_MissingDirAction", backupDirectory),
                Strings.Format("Step_NetworkCheck_MissingDir", backupDirectory));
        }

        ctx.Log.Information("Installing network drivers from {Directory}", backupDirectory);
        var add = await ctx.Services.PnpUtil.AddDriversAsync(backupDirectory, ct);
        if (!add.Success)
        {
            ctx.Log.Warning("pnputil /add-driver failed with exit code {ExitCode}: {Output}", add.ExitCode, add.Output);
            return StepResult.Fail(
                Strings.Get("Step_NetworkCheck_AddFailedAction"),
                Strings.Format("Step_NetworkCheck_AddFailed", backupDirectory, add.ExitCode));
        }

        for (var attempt = 1; attempt <= ProbeAttempts; attempt++)
        {
            if (await ctx.Services.Network.IsOnlineAsync(ct))
            {
                return StepResult.Success(Strings.Format("Step_NetworkCheck_Restored", backupDirectory));
            }

            if (attempt < ProbeAttempts)
            {
                await Task.Delay(retryDelay, ct);
            }
        }

        return StepResult.Fail(
            Strings.Get("Step_NetworkCheck_StillOfflineAction"),
            Strings.Format("Step_NetworkCheck_StillOffline", backupDirectory));
    }
}
