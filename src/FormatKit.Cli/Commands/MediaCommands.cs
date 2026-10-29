using System.CommandLine;
using System.Globalization;
using FormatKit.Cli.Localization;
using FormatKit.Core.Profiles;
using FormatKit.Media.Building;
using FormatKit.Media.Bundle;
using FormatKit.Media.Setup;

namespace FormatKit.Cli.Commands;

internal static class MediaCommands
{
    /// <summary>Test seam: the host language, keyboard and time zone. Null reads them from the registry.</summary>
    internal static Func<MachineLocale>? Machine { get; set; }

    /// <summary>Test seam: the executable copied into the media.</summary>
    internal static Func<string> ExePath { get; set; } = () => Environment.ProcessPath!;

    public static Command Create(CommandContext context)
    {
        var media = new Command("media", Strings.Get("Cmd_Media"));
        media.Subcommands.Add(CreateBuild(context));
        return media;
    }

    private static Command CreateBuild(CommandContext context)
    {
        var isoOption = new Option<string>("--iso") { Description = Strings.Get("Opt_MediaIso"), Required = true };
        var profileOption = new Option<string?>("--profile") { Description = Strings.Get("Opt_MediaProfile") };
        var outputOption = new Option<string>("--output") { Description = Strings.Get("Opt_MediaOutput"), Required = true };
        var build = new Command("build", Strings.Get("Cmd_MediaBuild")) { Options = { isoOption, profileOption, outputOption } };

        build.SetAction(async (parse, ct) =>
        {
            // R4: elevate first, so the work (and later the confirmation) runs in the elevated window.
            if (await context.EnsureElevatedAsync() is { } elevated)
                return elevated;

            var output = parse.GetValue(outputOption)!;
            if (!IsEmptyOrMissing(context, output))
            {
                context.Out.WriteLine(Strings.Format("Media_OutputNotEmpty", output));
                return ExitCodes.InvalidInput;
            }

            var (exit, request, prepared) = await PrepareAsync(context, parse.GetValue(isoOption)!, parse.GetValue(profileOption), ct);
            if (prepared is null)
                return exit;

            await using (prepared)
                return await BuildAsync(context, request!, prepared, output, ct);
        });
        return build;
    }

    private static async Task<int> BuildAsync(
        CommandContext context, MediaRequest request, PreparedMedia prepared, string output, CancellationToken ct)
    {
        var o = context.Out;
        var root = Path.GetPathRoot(Path.GetFullPath(output)) ?? output;
        long free;
        try
        {
            free = context.Env.Services.VirtualDisks.FreeBytes(root);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            o.WriteLine(Strings.Format("Media_OutputDriveUnreadable", root, ex.Message));
            return ExitCodes.InvalidInput;
        }

        if (free < prepared.RequiredBytes)
        {
            o.WriteLine(Strings.Format("Media_NotEnoughSpace", root, FormatSize(prepared.RequiredBytes), FormatSize(free)));
            return ExitCodes.InvalidInput;
        }

        var builder = new MediaBuilder(context.Env.Services, context.Env.Log);
        try
        {
            // The builder logs the generated answer file in full at Information level (spec 10).
            await builder.WriteAsync(prepared, request, output, new ConsoleProgress(o), ct);
        }
        catch (MediaWriteException ex)
        {
            context.Env.Log.Error(ex, "Writing the media to {Output} failed", output);
            o.WriteLine(Strings.Format("Media_BuildIncomplete", output, ex.Message));
            return ExitCodes.CompletedWithFailures;
        }

        o.WriteLine(Strings.Format("Media_BuildDone", output));
        o.WriteLine(Strings.Get("Media_PrepareReminder"));
        return ExitCodes.Ok;
    }

    /// <summary>
    /// Loads and checks the profile and its inputs, then mounts the ISO. Returns the exit code to use when
    /// the media is null (everything printed); otherwise the caller owns (and disposes) the mounted media.
    /// </summary>
    internal static async Task<(int Exit, MediaRequest? Request, PreparedMedia? Media)> PrepareAsync(
        CommandContext context, string iso, string? profilePath, CancellationToken ct)
    {
        var o = context.Out;
        var setup = new SetupOptions();
        ProfileBundle? bundle = null;

        if (profilePath is not null)
        {
            var profile = RunCommand.LoadProfile(context, profilePath);
            if (profile is null)
                return (ExitCodes.InvalidInput, null, null);

            var (options, errors) = SetupSectionParser.Parse(File.ReadAllText(profile.Path));
            if (options is null)
            {
                RunCommand.PrintErrors(context, errors);
                return (ExitCodes.InvalidInput, null, null);
            }

            setup = options;
            bundle = ProfileBundle.Plan(profile, CollectInputs(context, profile), context.Env.Services.Files);
            foreach (var missing in bundle.Missing)
                o.WriteLine(Strings.Format("Media_MissingInput", missing));
            foreach (var folder in bundle.Unreadable)
                o.WriteLine(Strings.Format("Media_UnreadableInput", folder));
            if (bundle.Missing.Count > 0 || bundle.Unreadable.Count > 0)
                return (ExitCodes.InvalidInput, null, null);
        }

        var machine = Machine?.Invoke() ?? MachineLocale.Current(context.Env.Services.Registry);
        var request = new MediaRequest(iso, setup, bundle, machine, ExePath());
        try
        {
            var prepared = await new MediaBuilder(context.Env.Services, context.Env.Log).PrepareAsync(request, ct);
            return (ExitCodes.Ok, request, prepared);
        }
        catch (MediaException ex)
        {
            o.WriteLine(ex.Message);
            return (ExitCodes.InvalidInput, null, null);
        }
    }

    /// <summary>Inputs of every enabled module; the bundle writes the profile itself.</summary>
    private static List<ExternalInput> CollectInputs(CommandContext context, LoadedProfile profile)
    {
        var inputs = new List<ExternalInput>();
        foreach (var module in context.Modules)
        {
            if (profile.Profile.Modules.TryGetValue(module.Name, out var section) && module.IsEnabled(section))
                inputs.AddRange(module.ExternalInputs(section, profile.Profile.BaseDirectory));
        }

        return inputs;
    }

    private static bool IsEmptyOrMissing(CommandContext context, string path)
    {
        var files = context.Env.Services.Files;
        if (files.FileExists(path))
            return false;
        if (!files.DirectoryExists(path))
            return true;
        return !files.EnumerateFileEntries(path, recursive: true).Any() && !files.EnumerateDirectories(path).Any();
    }

    private static string FormatSize(long bytes) => bytes >= 1L << 30
        ? string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 30):0.0} GB")
        : string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 20):0} MB");

    /// <summary>Prints one line per phase and one per 10 % of the bytes. Synchronous, so output keeps its order.</summary>
    private sealed class ConsoleProgress(TextWriter o) : IProgress<MediaProgress>
    {
        private MediaPhase? _phase;
        private long _decile;

        public void Report(MediaProgress value)
        {
            if (_phase != value.Phase)
            {
                _phase = value.Phase;
                o.WriteLine(Strings.Get("Media_Phase_" + value.Phase));
            }

            if (value.TotalBytes <= 0)
                return;
            var decile = Math.Min(10, value.CopiedBytes * 10 / value.TotalBytes);
            if (decile > _decile)
            {
                _decile = decile;
                o.WriteLine(Strings.Format("Media_Percent", decile * 10));
            }
        }
    }
}
