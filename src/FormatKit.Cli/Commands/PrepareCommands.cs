using System.CommandLine;
using FormatKit.Cli.Localization;
using FormatKit.Core.Profiles;
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

            var env = Environment();
            var volumes = await ListVolumesAsync(context, ct);
            var prepareContext = new PrepareContext(
                context.Env.Services,
                env,
                new WindowsVolume(env.SystemDrive, volumes),
                profile,
                CollectInputs(context, profile),
                context.Env.Elevation.IsElevated,
                context.Env.Log);

            var report = await new PrepareChecklist(Checks()).RunAsync(prepareContext, ct);
            ConsoleRenderer.Write(context.Out, report);

            var exit = report.HasBlockers ? ExitCodes.CompletedWithFailures : ExitCodes.Ok;
            if (parse.GetValue(outputOption) is { } outputPath
                && !WriteOutput(context, prepareContext.Windows, outputPath, report, env.ComputerName))
                return ExitCodes.InvalidInput;

            return exit;
        }));
        return check;
    }

    /// <summary>Read once per run and shared through the context; null when listing failed (other drives are then unknown).</summary>
    private static async Task<IReadOnlyList<Core.SystemAccess.VolumeInfo>?> ListVolumesAsync(CommandContext context, CancellationToken ct)
    {
        try
        {
            return await context.Env.Services.Volumes.ListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Env.Log.Warning(ex, "Could not list the volumes; the volume checks report them as not checked");
            return null;
        }
    }

    /// <summary>Inputs of every enabled module (the profile is already valid), plus the profile file itself.</summary>
    private static List<ExternalInput> CollectInputs(CommandContext context, LoadedProfile? profile)
    {
        var inputs = new List<ExternalInput>();
        if (profile is null)
            return inputs;

        foreach (var module in context.Modules)
        {
            if (profile.Profile.Modules.TryGetValue(module.Name, out var section) && module.IsEnabled(section))
                inputs.AddRange(module.ExternalInputs(section, profile.Profile.BaseDirectory));
        }

        inputs.Add(new ExternalInput("", profile.Path, ExternalInputKind.File));
        return inputs;
    }

    private static bool WriteOutput(
        CommandContext context, WindowsVolume windows, string path, PrepareReport report, string computerName)
    {
        var o = context.Out;
        if (windows.IsLost(path))
            o.WriteLine(Strings.Format("Prepare_OutputOnWindows", path));

        try
        {
            context.Env.Services.Files.WriteAllTextAtomic(
                path, MarkdownRenderer.Render(report, computerName, TimeProvider.System.GetLocalNow()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            context.Env.Log.Warning(ex, "Could not write the checklist to {Path}", path);
            o.WriteLine(Strings.Format("Prepare_OutputFailed", path, ex.Message));
            return false;
        }

        o.WriteLine(Strings.Format("Prepare_OutputWritten", path));
        return true;
    }
}
