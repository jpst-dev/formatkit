using System.CommandLine;
using FormatKit.Cleanup.Steps;
using FormatKit.Cli.Localization;
using FormatKit.Core.Profiles;
using FormatKit.Core.Running;
using FormatKit.Hosting;

namespace FormatKit.Cli.Commands;

internal static class RunCommand
{
    public static Command Create(CommandContext context)
    {
        var profileOption = new Option<string?>("--profile") { Description = Strings.Get("Opt_Profile") };
        var dryRun = new Option<bool>("--dry-run") { Description = Strings.Get("Opt_DryRun") };
        var resume = new Option<bool>("--resume") { Description = Strings.Get("Opt_Resume") };
        var countdown = new Option<int?>("--countdown") { Description = Strings.Get("Opt_Countdown") };
        var run = new Command("run", Strings.Get("Cmd_Run")) { Options = { profileOption, dryRun, resume, countdown } };

        run.SetAction(async (parse, ct) =>
        {
            var path = parse.GetValue(profileOption);
            var resuming = parse.GetValue(resume);
            var o = context.Out;

            if (path is not null && resuming)
            {
                o.WriteLine(Strings.Get("Run_UsageBoth"));
                return ExitCodes.InvalidInput;
            }

            if (path is null && !resuming)
            {
                o.WriteLine(Strings.Get("Run_UsageNeither"));
                return ExitCodes.InvalidInput;
            }

            var wait = parse.GetValue(countdown);
            if (wait is not null && (resuming || parse.GetValue(dryRun) || wait < 0))
            {
                o.WriteLine(Strings.Get("Run_CountdownUsage"));
                return ExitCodes.InvalidInput;
            }

            if (resuming)
            {
                var load = context.Env.StateStore.Load();
                switch (load.Status)
                {
                    case StateLoadStatus.None:
                        o.WriteLine(Strings.Get("Run_NothingToResume"));
                        await RunFlow.RemoveResumeTaskAsync(context, ct);
                        return ExitCodes.NothingToResume;
                    case StateLoadStatus.Corrupt:
                        o.WriteLine(Strings.Format("Run_StateCorrupt", load.Error ?? ""));
                        await RunFlow.RemoveResumeTaskAsync(context, ct);
                        return ExitCodes.NothingToResume;
                }

                path = load.State!.ProfilePath;
            }

            var profile = LoadProfile(context, path!);
            if (profile is null && resuming)
            {
                o.WriteLine(Strings.Get("Run_ResumeProfileInvalid"));
                await RunFlow.RemoveResumeTaskAsync(context, ct);
                return ExitCodes.NothingToResume;
            }

            if (profile is null)
                return ExitCodes.InvalidInput;

            // Before RunFlow, so before elevation: the UAC prompt appears only after the countdown. The elevated
            // child gets the same arguments, but its parent already counted down: it must not wait again.
            var elevatedChild = parse.GetValue<bool>(CliApp.ElevatedChildFlag);
            if (wait is > 0 && !elevatedChild && context.Env.Services.Console.CanPrompt)
            {
                var starter = context.Env.Countdown ?? new StartCountdown();
                if (!await starter.WaitAsync(profile.Profile.Name, TimeSpan.FromSeconds(wait.Value), ct))
                {
                    // The answer file passes --wait-on-exit, so the window waits for Enter once, in CliApp.
                    o.WriteLine(Strings.Format("Run_CountdownCancelled", CommandLine(path!)));
                    return ExitCodes.Ok;
                }
            }

            return await RunFlow.ExecuteAsync(context, profile, resuming, parse.GetValue(dryRun), ct);
        });
        return run;
    }

    private static string CommandLine(string profilePath) =>
        $"{Quote(MediaCommands.ExePath())} run --profile {Quote(Path.GetFullPath(profilePath))}";

    private static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;

    internal static LoadedProfile? LoadProfile(CommandContext context, string path)
    {
        var outcome = new ProfileService(context.Modules).Load(path);
        if (outcome.Profile is not null)
            return outcome.Profile;

        PrintErrors(context, outcome.Errors);
        return null;
    }

    internal static void PrintErrors(CommandContext context, IReadOnlyList<ProfileError> errors)
    {
        context.Out.WriteLine(Strings.Format("Profile_Errors", errors.Count));
        foreach (var error in errors)
        {
            context.Out.WriteLine(error.Path.Length == 0
                ? $"  {error.Message}"
                : $"  {error.Path}: {error.Message}");
        }
    }
}

internal static class RunFlow
{
    public static async Task<int> ExecuteAsync(
        CommandContext context, LoadedProfile profile, bool resume, bool dryRun, CancellationToken ct)
    {
        var o = context.Out;
        var plan = PlanService.Build(profile.Profile, context.Modules, context.Env.SimulateRebootAfterStep);

        if (dryRun)
        {
            o.WriteLine(Strings.Format("Plan_Header", profile.Profile.Name));
            foreach (var step in plan)
            {
                var admin = step.RequiresAdmin ? $" [{Strings.Get("Plan_Admin")}]" : "";
                o.WriteLine($"  {step.Id}  {step.Title}{admin}");
                foreach (var line in DryRun.Items(step))
                    o.WriteLine($"    {line}");
            }

            return ExitCodes.Ok;
        }

        if (plan.Any(s => s.RequiresAdmin) && await context.EnsureElevatedAsync() is { } exit)
            return exit;

        var session = new RunSession(context.Env.ToHost());
        RunSummary summary;
        try
        {
            summary = await session.RunAsync(profile, plan, resume, new ConsoleRunProgress(o), ct);
        }
        catch (OperationCanceledException)
        {
            o.WriteLine(Strings.Get("Cancelled_Run"));
            return ExitCodes.Cancelled;
        }

        if (session.ResumeTaskRemoved)
            o.WriteLine(Strings.Get("Run_ResumeTaskRemoved"));

        return Report(o, summary);
    }

    /// <summary>Best-effort removal of the automatic resume task when a resume ends early.</summary>
    public static async Task RemoveResumeTaskAsync(CommandContext context, CancellationToken ct)
    {
        if (await new RunSession(context.Env.ToHost()).RemoveResumeTaskAsync(ct))
            context.Out.WriteLine(Strings.Get("Run_ResumeTaskRemoved"));
    }

    private static int Report(TextWriter o, RunSummary summary)
    {
        switch (summary.Status)
        {
            case RunStatus.NothingToResume:
                o.WriteLine(Strings.Get("Run_NothingToResume"));
                return ExitCodes.NothingToResume;
            case RunStatus.StateCorrupt:
                o.WriteLine(Strings.Format("Run_StateCorrupt", summary.Error ?? ""));
                return ExitCodes.NothingToResume;
            case RunStatus.ProfileChanged:
                o.WriteLine(Strings.Get("Run_ProfileChanged"));
                return ExitCodes.NothingToResume;
        }

        foreach (var step in summary.Steps)
        {
            o.WriteLine($"[{Strings.Get("Outcome_" + step.Outcome)}] {step.Title}");
            foreach (var message in step.Messages)
                o.WriteLine($"    {message}");
            if (!string.IsNullOrWhiteSpace(step.NextAction))
                o.WriteLine($"    {Strings.Format("Run_NextAction", step.NextAction)}");
        }

        if (summary.ReportDirectory is not null)
            o.WriteLine(Strings.Format("Run_ReportDir", summary.ReportDirectory));

        switch (summary.Status)
        {
            case RunStatus.RebootPending:
                if (summary.ResumeScheduleFailed)
                    o.WriteLine(Strings.Get("Run_ResumeNotScheduled"));
                else
                    o.WriteLine(Strings.Get("Run_RebootPending"));
                return ExitCodes.RebootPending;
            case RunStatus.StoppedOnCriticalFailure:
                o.WriteLine(Strings.Get("Run_Status_StoppedOnCriticalFailure"));
                return ExitCodes.StoppedOnCriticalFailure;
            case RunStatus.CompletedWithFailures:
                o.WriteLine(Strings.Get("Run_Status_CompletedWithFailures"));
                return ExitCodes.CompletedWithFailures;
            default:
                o.WriteLine(Strings.Get("Run_Status_Completed"));
                return ExitCodes.Ok;
        }
    }
}
