using System.CommandLine;
using System.Globalization;
using FormatKit.Cli.Localization;
using FormatKit.Core.SystemAccess;
using FormatKit.Hosting;
using FormatKit.Media.Building;
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
            if (!MediaFolder.IsEmptyOrMissing(context.Env.Services.Files, output))
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
                // The running exe counts too: started from the target stick, the erase would remove it.
                var sources = MediaRunner.SourcePaths(request!, profilePath);

                return await UsbAsync(context, request!, prepared, sources, parse.GetValue(diskOption),
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

        // A running Windows always has a disk: an empty list means the query failed, not that no stick is plugged in.
        if (candidates.Count == 0)
        {
            o.WriteLine(Strings.Get("Media_UsbWindowsNoDisks"));
            return ExitCodes.InvalidInput;
        }

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

        string root;
        try
        {
            var erased = await target.ConfirmAndPrepareAsync(
                candidate, new ConsoleDiskConfirmation(o), allowHardDisk, sources, prepared.RequiredBytes, ct);
            if (erased is null)
            {
                o.WriteLine(Strings.Get("Media_UsbCancelled"));
                return ExitCodes.InvalidInput;
            }

            root = erased;
        }
        catch (DiskChangedException ex)
        {
            // The exception text is English; the user sees the localized message.
            context.Env.Log.Information(ex, "Disk {Number} changed before the erase", number);
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
            context.Env.Log.Information(ex, "Erasing disk {Number} failed", number);
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
            context.Env.Log.Information(ex, "Writing the media to the USB stick {Root} failed", root);
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
        var listing = UsbListing.Of(candidates);
        if (listing.Shown.Count == 0)
        {
            o.WriteLine(Strings.Get("Media_UsbNoDisks"));
        }
        else
        {
            o.WriteLine(Strings.Get("Media_UsbListHeader"));
            foreach (var c in listing.Shown)
            {
                o.WriteLine(DiskTitle(c.Disk));
                PrintVolumes(o, c.Disk);
                o.WriteLine(c.Refusal == UsbRefusal.None
                    ? Strings.Get("Media_UsbEligible")
                    : Strings.Format("Media_UsbNotUsable", RefusalText(c)));
            }
        }

        if (listing.HiddenHardDisks > 0)
            o.WriteLine(Strings.Format("Media_UsbHardDisksHidden", listing.HiddenHardDisks));
    }

    private static string DiskTitle(DiskInfo d) => Strings.Format("Media_UsbDiskTitle", d.Number, d.FriendlyName,
        MediaFolder.FormatSize(d.SizeBytes), d.BusType, Strings.Get(d.IsRemovable ? "Media_UsbRemovable" : "Media_UsbFixed"));

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
        var space = MediaFolder.ReadSpace(context.Env.Services.VirtualDisks, output);
        if (space.Error is not null)
        {
            o.WriteLine(Strings.Format("Media_OutputDriveUnreadable", space.Root, space.Error));
            return ExitCodes.InvalidInput;
        }

        if (space.FreeBytes < prepared.RequiredBytes)
        {
            o.WriteLine(Strings.Format("Media_NotEnoughSpace", space.Root,
                MediaFolder.FormatSize(prepared.RequiredBytes), MediaFolder.FormatSize(space.FreeBytes)));
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
            context.Env.Log.Information(ex, "Writing the media to {Output} failed", output);
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
        var machine = Machine?.Invoke() ?? MachineLocale.Current(context.Env.Services.Registry);
        var runner = new MediaRunner(context.Env.Services, context.Env.Log, context.Modules);
        var outcome = await runner.PrepareAsync(iso, profilePath, expectedHash: null, machine, ExePath(), ct);
        if (outcome.Media is not null)
            return (ExitCodes.Ok, outcome.Request, outcome.Media);

        if (outcome.ProfileErrors.Count > 0)
            RunCommand.PrintErrors(context, outcome.ProfileErrors);
        foreach (var missing in outcome.MissingInputs)
            o.WriteLine(Strings.Format("Media_MissingInput", missing));
        foreach (var folder in outcome.UnreadableInputs)
            o.WriteLine(Strings.Format("Media_UnreadableInput", folder));
        if (outcome.Error is not null)
            o.WriteLine(outcome.Error);
        return (ExitCodes.InvalidInput, null, null);
    }

    /// <summary>Prints what the user is about to erase and reads the typed confirmation.</summary>
    private sealed class ConsoleDiskConfirmation(TextWriter o) : IDiskConfirmation
    {
        public Task<string?> AskAsync(UsbCandidate disk, CancellationToken ct)
        {
            o.WriteLine(DiskTitle(disk.Disk) + " - " + Strings.Get("Media_UsbEraseWarning"));
            PrintVolumes(o, disk.Disk);
            if (!disk.Disk.IsRemovable)
                o.WriteLine(Strings.Get("Media_UsbIsHardDisk"));

            o.Write(Strings.Format("Media_UsbTypeNumber", disk.Disk.Number.ToString(CultureInfo.InvariantCulture)));
            return Task.FromResult(ReadLine());
        }
    }

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
