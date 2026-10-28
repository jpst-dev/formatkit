using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Programs.Catalog;
using FormatKit.Programs.Localization;

namespace FormatKit.Programs.Steps;

/// <summary>Installs (or upgrades) the winget packages of one category, or of <c>programs.other</c> (spec 6).</summary>
internal sealed class WingetProgramsStep(string id, string title, IReadOnlyList<SelectedProgram> programs, bool upgrade) : IStep
{
    private const string RegisterAppInstaller =
        "Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe";

    /// <summary>The programs this step installs, for the dry-run listing.</summary>
    public IReadOnlyList<SelectedProgram> Programs => programs;

    public string Id => id;

    public string Title => title;

    public bool RequiresAdmin => true;

    public bool IsCritical => false;

    public async Task<bool> CheckAsync(StepContext ctx, CancellationToken ct)
    {
        if (upgrade)
            return false;

        try
        {
            foreach (var program in programs)
            {
                if (!await ctx.Services.Winget.IsInstalledAsync(Ref(program), ct).ConfigureAwait(false))
                    return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ctx.Log.Warning(ex, "Could not check programs for step {Step}", id);
            return false;
        }
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var messages = new List<string>();

        if (!await EnsureWingetAsync(ctx, ct).ConfigureAwait(false))
        {
            var missing = Strings.Get("NextAction_WingetMissing");
            messages.AddRange(programs.Select(p => Line(p, missing)));
            return StepResult.Fail(missing, [.. messages]);
        }

        bool anyFailed = false, reboot = false, anyNoUser = false;

        foreach (var program in programs)
        {
            ct.ThrowIfCancellationRequested();

            string text;
            try
            {
                var outcome = await InstallOneAsync(program, ctx, ct).ConfigureAwait(false);
                text = outcome.Text;
                anyFailed |= outcome.Failed;
                reboot |= outcome.Reboot;
                anyNoUser |= outcome.NoUser;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ctx.Log.Warning(ex, "Program {Program} failed", program.Key);
                text = Strings.Get("Item_Failed_Error");
                anyFailed = true;
            }

            messages.Add(Line(program, text));
        }

        if (anyFailed)
        {
            var next = anyNoUser ? Strings.Get("NextAction_SignIn") : Strings.Get("NextAction_SeeLog");
            return StepResult.Fail(next, [.. messages]);
        }

        return reboot ? StepResult.Reboot([.. messages]) : StepResult.Success([.. messages]);
    }

    private async Task<bool> EnsureWingetAsync(StepContext ctx, CancellationToken ct)
    {
        if (await ctx.Services.Winget.IsAvailableAsync(ct).ConfigureAwait(false))
            return true;

        try
        {
            await ctx.Services.Processes.RunAsync(
                new ProcessSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", RegisterAppInstaller], Timeouts.Query),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ctx.Log.Warning(ex, "Registering App Installer failed");
        }

        return await ctx.Services.Winget.IsAvailableAsync(ct).ConfigureAwait(false);
    }

    private async Task<Outcome> InstallOneAsync(SelectedProgram program, StepContext ctx, CancellationToken ct)
    {
        var package = Ref(program);
        var user = program.Package.Context == InstallContext.User;

        if (user && !ctx.Services.UserSession.HasInteractiveUser)
            return new Outcome(Strings.Get("Item_Failed_NoUser"), Failed: true, NoUser: true);

        // A per-user package is invisible to this process, so those items always go to the user's session;
        // winget answers "already installed" itself.
        var installed = !user && await ctx.Services.Winget.IsInstalledAsync(package, ct).ConfigureAwait(false);
        if (installed && !upgrade)
            return new Outcome(Strings.Get("Item_AlreadyInstalled"));

        WingetResult result;
        if (user)
        {
            // Install first: winget answers "already installed" itself, and only then does an upgrade make sense.
            result = await RunAsUserAsync(WingetClient.InstallArguments(package), upgrade: false, ctx, ct).ConfigureAwait(false);
            if (upgrade && result.Status == WingetStatus.AlreadyInstalled)
                result = await RunAsUserAsync(WingetClient.UpgradeArguments(package), upgrade: true, ctx, ct).ConfigureAwait(false);
        }
        else if (installed)
        {
            result = await ctx.Services.Winget.UpgradeAsync(package, ct).ConfigureAwait(false);
        }
        else
        {
            result = await ctx.Services.Winget.InstallAsync(package, ct).ConfigureAwait(false);
        }

        return Describe(result);
    }

    private static async Task<WingetResult> RunAsUserAsync(IReadOnlyList<string> args, bool upgrade, StepContext ctx, CancellationToken ct)
    {
        var run = await ctx.Services.UserContext.RunAsync(new ProcessSpec("winget.exe", args, Timeouts.Install), ct).ConfigureAwait(false);
        return new WingetResult(
            WingetClient.MapExitCode(run.ExitCode, upgrade, run.TimedOut), run.ExitCode, run.StdOut + run.StdErr, run.TimedOut);
    }

    private static Outcome Describe(WingetResult result)
    {
        if (result.TimedOut)
            return new Outcome(Strings.Get("Item_Failed_Timeout"), Failed: true);

        return result.Status switch
        {
            WingetStatus.Installed => new Outcome(Strings.Get("Item_Installed")),
            WingetStatus.AlreadyInstalled => new Outcome(Strings.Get("Item_AlreadyInstalled")),
            WingetStatus.Upgraded => new Outcome(Strings.Get("Item_Upgraded")),
            WingetStatus.UpToDate => new Outcome(Strings.Get("Item_UpToDate")),
            WingetStatus.RebootRequired => new Outcome(Strings.Get("Item_RebootRequired"), Reboot: true),
            WingetStatus.NotFound => new Outcome(Strings.Get("Item_Failed_NotFound"), Failed: true),
            WingetStatus.NoNetwork => new Outcome(Strings.Get("Item_Failed_NoNetwork"), Failed: true),
            WingetStatus.BlockedByPolicy => new Outcome(Strings.Get("Item_Failed_Policy"), Failed: true),
            _ => new Outcome(Strings.Format("Item_Failed_Code", FormatCode(result.ExitCode)), Failed: true),
        };
    }

    /// <summary>Exit codes are shown in hex, as winget documents them (for example 0x8A150014).</summary>
    internal static string FormatCode(int exitCode) => $"0x{exitCode:X8}";

    private static string Line(SelectedProgram program, string text) => $"{program.DisplayName}: {text}";

    private static WingetPackageRef Ref(SelectedProgram program) =>
        new(program.Package.Id, program.Package.Source, program.Package.Version);

    private readonly record struct Outcome(string Text, bool Failed = false, bool Reboot = false, bool NoUser = false);
}
