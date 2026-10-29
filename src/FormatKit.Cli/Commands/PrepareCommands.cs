using System.CommandLine;
using FormatKit.Cli.Localization;
using FormatKit.Core.Profiles;
using FormatKit.Hosting;
using FormatKit.Prepare;
using FormatKit.Prepare.Rendering;

namespace FormatKit.Cli.Commands;

internal static class PrepareCommands
{
    /// <summary>Test seam: the checks the command runs.</summary>
    internal static Func<IReadOnlyList<IPrepareCheck>> Checks { get; set; } = PrepareChecks.All;

    /// <summary>Test seam: the machine facts (user folders, system drive, computer name).</summary>
    internal static Func<PrepareEnvironment> Environment { get; set; } = PrepareEnvironment.Current;

    public static Command Create(CommandContext context)
    {
        var prepare = new Command("prepare", Strings.Get("Cmd_Prepare"));
        prepare.Subcommands.Add(CreateCheck(context));
        return prepare;
    }

    private static Command CreateCheck(CommandContext context)
    {
        var profileOption = new Option<string?>("--profile") { Description = Strings.Get("Opt_PrepareProfile") };
        var outputOption = new Option<string?>("--output") { Description = Strings.Get("Opt_PrepareOutput") };
        var check = new Command("check", Strings.Get("Cmd_PrepareCheck")) { Options = { profileOption, outputOption } };

        check.SetAction(async (parse, ct) => await Output.GuardSystemQueryAsync(context.Out, async () =>
        {
            LoadedProfile? profile = null;
            if (parse.GetValue(profileOption) is { } profilePath)
            {
                profile = RunCommand.LoadProfile(context, profilePath);
                if (profile is null)
                    return ExitCodes.InvalidInput;
            }

            var runner = new PrepareRunner(context.Env.ToHost(), context.Modules, Checks, Environment);
            var run = await runner.RunAsync(profile, ct);
            ConsoleRenderer.Write(context.Out, run.Report);

            var exit = run.Report.HasBlockers ? ExitCodes.CompletedWithFailures : ExitCodes.Ok;
            if (parse.GetValue(outputOption) is { } outputPath && !WriteOutput(context, runner, run, outputPath))
                return ExitCodes.InvalidInput;

            return exit;
        }));
        return check;
    }

    private static bool WriteOutput(CommandContext context, PrepareRunner runner, PrepareRun run, string path)
    {
        var o = context.Out;
        var write = runner.WriteMarkdown(run, path);
        if (write.OnWindowsDrive)
            o.WriteLine(Strings.Format("Prepare_OutputOnWindows", path));
        if (write.Error is not null)
        {
            o.WriteLine(Strings.Format("Prepare_OutputFailed", path, write.Error));
            return false;
        }

        o.WriteLine(Strings.Format("Prepare_OutputWritten", path));
        return true;
    }
}
