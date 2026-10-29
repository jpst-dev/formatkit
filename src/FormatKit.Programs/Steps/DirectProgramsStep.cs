using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Programs.Localization;

namespace FormatKit.Programs.Steps;

/// <summary>Runs the profile's direct installers, skipping those a previous run already finished.</summary>
internal sealed class DirectProgramsStep(IReadOnlyList<DirectEntry> entries, AppPaths paths) : IStep
{
    /// <summary>The installers this step runs, for the dry-run listing.</summary>
    public IReadOnlyList<DirectEntry> Entries => entries;

    public string Id => "programs.direct";

    public string Title => Strings.Get("Step_Title_Direct");

    public bool RequiresAdmin => true;

    public bool IsCritical => false;

    public Task<bool> CheckAsync(StepContext ctx, CancellationToken ct)
    {
        try
        {
            var progress = new DirectProgress(ctx.Services.Files, paths, ctx.Log);
            return Task.FromResult(entries.All(e => progress.IsDone(KeyOf(e))));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ctx.Log.Warning(ex, "Could not check direct installers");
            return Task.FromResult(false);
        }
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var progress = new DirectProgress(ctx.Services.Files, paths, ctx.Log);
        var messages = new List<string>();
        bool anyFailed = false, reboot = false;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            string text;
            try
            {
                var key = KeyOf(entry);
                if (progress.IsDone(key))
                {
                    text = Strings.Get("Item_AlreadyInstalled");
                }
                else
                {
                    var source = new DirectSource(entry.Name, entry.Url is null ? null : new Uri(entry.Url), entry.Path, entry.Sha256, entry.Args);
                    // The run's token may cancel the download only; DirectInstaller never lets it kill the installer.
                    var result = await ctx.Services.Direct.InstallAsync(source, ct).ConfigureAwait(false);
                    if (result.Status is not (DirectStatus.Installed or DirectStatus.RebootRequired))
                    {
                        ctx.Log.Warning("Direct installer {Name}: {Status}, exit code {ExitCode}: {Message}",
                            entry.Name, result.Status, result.ExitCode, result.Message);
                    }

                    switch (result.Status)
                    {
                        case DirectStatus.Installed:
                            progress.MarkDone(key);
                            text = Strings.Get("Item_Installed");
                            break;
                        case DirectStatus.RebootRequired:
                            progress.MarkDone(key);
                            reboot = true;
                            text = Strings.Get("Item_RebootRequired");
                            break;
                        case DirectStatus.HashMismatch:
                            anyFailed = true;
                            text = Strings.Get("Item_Failed_HashMismatch");
                            break;
                        case DirectStatus.DownloadFailed:
                            anyFailed = true;
                            text = Strings.Get("Item_Failed_Download");
                            break;
                        case DirectStatus.Failed when result.TimedOut:
                            anyFailed = true;
                            text = Strings.Get("Item_Failed_Timeout");
                            break;
                        default:
                            anyFailed = true;
                            text = Strings.Format("Item_Failed_Code", WingetProgramsStep.FormatCode(result.ExitCode));
                            break;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ctx.Log.Warning(ex, "Direct installer {Name} failed", entry.Name);
                anyFailed = true;
                text = Strings.Get("Item_Failed_Error");
            }

            messages.Add($"{entry.Name}: {text}");
        }

        if (anyFailed)
            return StepResult.Fail(Strings.Get("NextAction_SeeLog"), [.. messages]);

        return reboot ? StepResult.Reboot([.. messages]) : StepResult.Success([.. messages]);
    }

    /// <summary>The progress key: name plus hash when pinned, otherwise name plus where it comes from.</summary>
    internal static string KeyOf(DirectEntry entry) =>
        entry.Sha256 is not null
            ? $"{entry.Name}|{entry.Sha256.ToLowerInvariant()}"
            : $"{entry.Name}|{entry.Path ?? entry.Url}";
}
