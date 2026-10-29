using System.CommandLine;
using System.Globalization;
using FormatKit.Cli.Localization;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Profiles;
using FormatKit.Media.Building;
using FormatKit.Media.Bundle;
using FormatKit.Media.Setup;
using FormatKit.Media.Usb;

namespace FormatKit.Cli.Commands;

internal static class MediaCommands
{
    /// <summary>Test seam: the host language, keyboard and time zone. Null reads them from the registry.</summary>
    internal static Func<MachineLocale>? Machine { get; set; }

    /// <summary>Test seam: the executable copied into the media.</summary>
    internal static Func<string> ExePath { get; set; } = () => Environment.ProcessPath!;

    /// <summary>Test seam: reads the typed confirmation.</summary>
    internal static Func<string?> ReadLine { get; set; } = Console.ReadLine;

    public static Command Create(CommandContext context)
    {
        var media = new Command("media", Strings.Get("Cmd_Media"));
        media.Subcommands.Add(CreateBuild(context));
        media.Subcommands.Add(CreateUsb(context));
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

    private static Command CreateUsb(CommandContext context)
    {
        var isoOption = new Option<string>("--iso") { Description = Strings.Get("Opt_MediaIso"), Required = true };
        var profileOption = new Option<string?>("--profile") { Description = Strings.Get("Opt_MediaProfile") };
        var diskOption = new Option<int?>("--disk") { Description = Strings.Get("Opt_MediaDisk") };
        var hardDiskOption = new Option<bool>("--allow-hard-disk") { Description = Strings.Get("Opt_MediaAllowHardDisk") };
        var usb = new Command("usb", Strings.Get("Cmd_MediaUsb")) { Options = { isoOption, profileOption, diskOption, hardDiskOption } };

        usb.SetAction(async (parse, ct) =>
        {
            // R4: elevate first, so the listing, the confirmation and the work run in the elevated window.
            if (await context.EnsureElevatedAsync() is { } elevated)
                return elevated;

            var profilePath = parse.GetValue(profileOption);
            var (exit, request, prepared) = await PrepareAsync(context, parse.GetValue(isoOption)!, profilePath, ct);
            if (prepared is null)
                return exit;

            await using (prepared)
            {
                var sources = new List<string> { request!.IsoPath };
                if (profilePath is not null)
                    sources.Add(profilePath);
                if (request.Bundle is { } bundle)
                    sources.AddRange(bundle.Items.Select(i => i.Input.ResolvedPath));

                return await UsbAsync(context, request, prepared, sources, parse.GetValue(diskOption),
                    parse.GetValue(hardDiskOption), ct);
            }
        });
        return usb;
    }

    private static async Task<int> UsbAsync(CommandContext context, MediaRequest request, PreparedMedia prepared,
        IReadOnlyList<string> sources, int? diskNumber, bool allowHardDisk, CancellationToken ct)
    {
        var o = context.Out;
        var target = new UsbTarget(context.Env.Services.Disks);
        var candidates = await target.ListAsync(allowHardDisk, sources, prepared.RequiredBytes, ct);

        if (diskNumber is null)
        {
            PrintUsbDisks(o, candidates);
            return ExitCodes.InvalidInput;
        }

        var number = diskNumber.Value;
        var candidate = candidates.FirstOrDefault(c => c.Disk.Number == number);
        if (candidate is null)
        {
            o.WriteLine(Strings.Format("Media_UsbDiskNotFound", number));
            return ExitCodes.InvalidInput;
        }

        if (candidate.Refusal != UsbRefusal.None)
        {
            o.WriteLine(Strings.Format("Media_UsbRefused", number, RefusalText(candidate)));
            return ExitCodes.InvalidInput;
        }

        if (!context.Env.Services.Console.CanPrompt)
        {
            o.WriteLine(Strings.Get("Media_UsbNoConsole"));
            return ExitCodes.InvalidInput;
        }

        o.WriteLine(DiskTitle(candidate.Disk) + " - " + Strings.Get("Media_UsbEraseWarning"));
        PrintVolumes(o, candidate.Disk);
        if (!candidate.Disk.IsRemovable)
            o.WriteLine(Strings.Get("Media_UsbIsHardDisk"));

        var expected = number.ToString(CultureInfo.InvariantCulture);
        o.Write(Strings.Format("Media_UsbTypeNumber", expected));
        var answer = ReadLine();
        if (answer is null || !string.Equals(answer.Trim(), expected, StringComparison.Ordinal))
        {
            o.WriteLine(Strings.Get("Media_UsbCancelled"));
            return ExitCodes.InvalidInput;
        }

        string root;
        try
        {
            root = await target.PrepareAsync(candidate, allowHardDisk, sources, prepared.RequiredBytes, ct);
        }
        catch (DiskChangedException ex)
        {
            // The exception text is English; the user sees the localized message.
            context.Env.Log.Warning(ex, "Disk {Number} changed before the erase", number);
            o.WriteLine(Strings.Format("Media_UsbDiskChanged", number));
            return ExitCodes.InvalidInput;
        }
        catch (OperationCanceledException)
        {
            o.WriteLine(Strings.Format("Media_UsbIncomplete", Strings.Get("Media_UsbInterrupted")));
            throw;
        }
        catch (Exception ex)
        {
            // The erase may have started: the stick must be treated as incomplete. The text stays in the log only.
            context.Env.Log.Error(ex, "Erasing disk {Number} failed", number);
            o.WriteLine(Strings.Format("Media_UsbIncomplete", Strings.Get("Media_UsbEraseFailed")));
            return ExitCodes.CompletedWithFailures;
        }

        var builder = new MediaBuilder(context.Env.Services, context.Env.Log);
        try
        {
            await builder.WriteAsync(prepared, request, root, new ConsoleProgress(o), ct);
        }
        catch (MediaWriteException ex)
        {
            context.Env.Log.Error(ex, "Writing the media to the USB stick {Root} failed", root);
            o.WriteLine(Strings.Format("Media_UsbIncomplete", ex.Message));
            return ExitCodes.CompletedWithFailures;
        }
        catch (OperationCanceledException)
        {
            o.WriteLine(Strings.Format("Media_UsbIncomplete", Strings.Get("Media_UsbInterrupted")));
            throw;
        }

        o.WriteLine(Strings.Get("Media_UsbDone"));
        o.WriteLine(Strings.Get("Media_PrepareReminder"));
        return ExitCodes.Ok;
    }

    private static void PrintUsbDisks(TextWriter o, IReadOnlyList<UsbCandidate> candidates)
    {
        var allUsb = candidates.Where(c => string.Equals(c.Disk.BusType, "USB", StringComparison.OrdinalIgnoreCase)).ToList();
        var hidden = allUsb.Count(c => c.Refusal == UsbRefusal.HardDisk);
        var usb = allUsb.Where(c => c.Refusal != UsbRefusal.HardDisk).ToList();
        if (usb.Count == 0)
        {
            o.WriteLine(Strings.Get("Media_UsbNoDisks"));
        }
        else
        {
            o.WriteLine(Strings.Get("Media_UsbListHeader"));
            foreach (var c in usb)
            {
                o.WriteLine(DiskTitle(c.Disk));
                PrintVolumes(o, c.Disk);
                o.WriteLine(c.Refusal == UsbRefusal.None
                    ? Strings.Get("Media_UsbEligible")
                    : Strings.Format("Media_UsbNotUsable", RefusalText(c)));
            }
        }

        if (hidden > 0)
            o.WriteLine(Strings.Format("Media_UsbHardDisksHidden", hidden));
    }

    private static string DiskTitle(DiskInfo d) => Strings.Format("Media_UsbDiskTitle", d.Number, d.FriendlyName,
        FormatSize(d.SizeBytes), d.BusType, Strings.Get(d.IsRemovable ? "Media_UsbRemovable" : "Media_UsbFixed"));

    private static void PrintVolumes(TextWriter o, DiskInfo d)
    {
        if (d.Volumes.Count == 0)
        {
            o.WriteLine(Strings.Get("Media_UsbNoVolumes"));
            return;
        }

        foreach (var v in d.Volumes)
        {
            o.WriteLine(v.Letter is { } letter
                ? Strings.Format("Media_UsbVolume", char.ToUpperInvariant(letter), v.Label ?? string.Empty)
                : Strings.Format("Media_UsbVolumeNoLetter", v.Label ?? string.Empty));
        }
    }

    private static string RefusalText(UsbCandidate c) =>
        Strings.Format("Media_UsbRefusal_" + c.Refusal, string.Join(", ", c.SourcesOnDisk));

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
