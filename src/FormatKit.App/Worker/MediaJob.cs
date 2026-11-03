using System.IO;
using System.Text;
using FormatKit.App.Localization;
using FormatKit.Core.Profiles;
using FormatKit.Core.SystemAccess;
using FormatKit.Hosting;
using FormatKit.Media.Building;
using FormatKit.Media.Setup;
using FormatKit.Media.Usb;

namespace FormatKit.App.Worker;

/// <summary>
/// The worker's media job: what <c>media build</c> and <c>media usb</c> do, with pipe messages for the console. The
/// rules, the typed-number check and the re-check stay in <c>FormatKit.Media</c> and <see cref="MediaRunner"/>.
/// <paramref name="stop"/> (the window's Cancel, or the window gone) ends the wait for a disk and the re-check with nothing
/// erased, and the writing between files; the erase itself gets only <paramref name="ct"/>, because an interrupted <c>diskpart</c>
/// leaves the disk in an unknown state. The ISO stays mounted until the job ends, and the result goes out after the
/// dismount, so a dismount failure reaches the window first.
/// </summary>
internal sealed class MediaJob(
    HostEnvironment env,
    IReadOnlyList<IModule> modules,
    MessageChannel channel,
    Task<ConfirmDisk> diskChoice,
    string uiExePath,
    CancellationToken stop,
    CancellationToken ct)
{
    /// <summary>The result to send: <see cref="MediaFinished"/>, or <see cref="Failed"/> when nothing was touched.</summary>
    public async Task<WorkerMessage> RunAsync(MediaJobRequest job)
    {
        var files = env.Services.Files;
        if (job.Output is not null && !MediaFolder.IsEmptyOrMissing(files, job.Output))
            return new Failed(Strings.Format("Media_OutputNotEmpty", job.Output));

        // The first logon runs formatkit.exe (UnattendBuilder), so the media needs the CLI next to the window's exe.
        var cliExe = Path.Combine(Path.GetDirectoryName(uiExePath) ?? string.Empty, "formatkit.exe");
        if (job.Profile is not null && !files.FileExists(cliExe))
            return new Failed(Strings.Format("Media_CliExeMissing", cliExe));

        // A killed Mount-DiskImage could leave the ISO mounted, so the stop is checked after this, not during it.
        var outcome = await new MediaRunner(env.Services, env.Log, modules).PrepareAsync(
            job.Iso, job.Profile, job.ProfileHash, MachineLocale.Current(env.Services.Registry), cliExe, ct).ConfigureAwait(false);
        if (outcome.Media is not { } prepared)
            return Refusal(outcome);

        WorkerMessage result;
        await using (prepared.ConfigureAwait(false))
        {
            result = job.Output is { } output
                ? await WriteFolderAsync(outcome.Request!, prepared, output).ConfigureAwait(false)
                : await WriteUsbAsync(job, outcome.Request!, prepared).ConfigureAwait(false);
        }

        return result;
    }

    private static Failed Refusal(MediaPrepareOutcome outcome)
    {
        if (outcome.ProfileChanged)
            return new Failed(Strings.Get("Worker_ProfileChanged"), FailureReason.ProfileChanged);
        if (outcome.ProfileErrors.Count > 0)
            return new Failed(Strings.Format("Worker_ProfileErrors", outcome.ProfileErrors.Count) + "\n" + ErrorLines(outcome.ProfileErrors));
        if (outcome.Error is not null)
            return new Failed(outcome.Error);

        var lines = outcome.MissingInputs.Select(m => Strings.Format("Media_MissingInput", m))
            .Concat(outcome.UnreadableInputs.Select(f => Strings.Format("Media_UnreadableInput", f)));
        return new Failed(string.Join('\n', lines));
    }

    private static string ErrorLines(IReadOnlyList<ProfileError> errors)
    {
        var text = new StringBuilder();
        foreach (var error in errors)
        {
            if (text.Length > 0) text.Append('\n');
            text.Append(error.Path.Length == 0 ? error.Message : $"{error.Path}: {error.Message}");
        }

        return text.ToString();
    }

    private async Task<WorkerMessage> WriteFolderAsync(MediaRequest request, PreparedMedia prepared, string output)
    {
        var space = MediaFolder.ReadSpace(env.Services.VirtualDisks, output);
        if (space.Error is not null)
            return new Failed(Strings.Format("Media_OutputDriveUnreadable", space.Root, space.Error));
        if (space.FreeBytes < prepared.RequiredBytes)
        {
            return new Failed(Strings.Format("Media_NotEnoughSpace", space.Root,
                MediaFolder.FormatSize(prepared.RequiredBytes), MediaFolder.FormatSize(space.FreeBytes)));
        }

        try
        {
            await WriteAsync(prepared, request, output).ConfigureAwait(false);
        }
        catch (MediaWriteException ex)
        {
            env.Log.Information(ex, "Writing the media to {Output} failed", output);
            return new MediaFinished(MediaFinishedStatus.Incomplete, Strings.Format("Media_FolderIncomplete", output, ex.Message));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            env.Log.Information("Media: stopped while writing to {Output}", output);
            return new MediaFinished(MediaFinishedStatus.Incomplete, Strings.Format("Media_FolderIncomplete", output, Strings.Get("Media_Interrupted")));
        }

        return new MediaFinished(MediaFinishedStatus.Done, Strings.Format("Media_FolderDone", output));
    }

    private async Task<WorkerMessage> WriteUsbAsync(MediaJobRequest job, MediaRequest request, PreparedMedia prepared)
    {
        // The running exe counts too: started from the target stick, the erase would remove it.
        var sources = MediaRunner.SourcePaths(request, job.Profile, uiExePath);
        var target = new UsbTarget(env.Services.Disks);
        var candidates = await target.ListAsync(job.AllowHardDisk, sources, prepared.RequiredBytes, ct).ConfigureAwait(false);

        // A running Windows always has a disk: an empty list means the query failed, not that no stick is plugged in.
        if (candidates.Count == 0)
            return new Failed(Strings.Get("Media_NoDisksAtAll"));

        var listing = UsbListing.Of(candidates);
        await channel.SendAsync(new Disks([.. listing.Shown.Select(Row)], listing.HiddenHardDisks), ct).ConfigureAwait(false);

        ConfirmDisk choice;
        try
        {
            choice = await diskChoice.WaitAsync(stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            env.Log.Information("Media: stopped before a disk was confirmed; nothing was erased");
            return new MediaFinished(MediaFinishedStatus.Cancelled, Strings.Get("Media_Cancelled"));
        }

        var disk = listing.Shown.FirstOrDefault(c => c.Disk.UniqueId == choice.UniqueId);
        if (disk is null || stop.IsCancellationRequested)
        {
            env.Log.Information("Media: nothing to erase (stopped, or a disk that is not offered was confirmed)");
            return new MediaFinished(MediaFinishedStatus.Cancelled, Strings.Get("Media_Cancelled"));
        }

        var number = disk.Disk.Number;
        string root;
        try
        {
            var erased = await target.ConfirmAndPrepareAsync(
                disk, new TypedAnswer(choice.TypedNumber), job.AllowHardDisk, sources, prepared.RequiredBytes, ct, stop).ConfigureAwait(false);
            if (erased is null)
            {
                env.Log.Information("Media: disk {Number} was not confirmed, or the job stopped first; nothing was erased", number);
                return new MediaFinished(MediaFinishedStatus.Cancelled, Strings.Get("Media_Cancelled"));
            }

            root = erased;
        }
        catch (DiskChangedException ex)
        {
            // The exception text is English; the user sees the localized message.
            env.Log.Information(ex, "Disk {Number} changed before the erase", number);
            return new MediaFinished(MediaFinishedStatus.DiskChanged, Strings.Format("Media_DiskChanged", number));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The erase may have started: the stick must be treated as incomplete. The text stays in the log only.
            env.Log.Information(ex, "Erasing disk {Number} failed", number);
            return new MediaFinished(MediaFinishedStatus.Incomplete, Strings.Format("Media_UsbIncomplete", Strings.Get("Media_EraseFailed")));
        }

        try
        {
            await WriteAsync(prepared, request, root).ConfigureAwait(false);
        }
        catch (MediaWriteException ex)
        {
            env.Log.Information(ex, "Writing the media to the USB stick {Root} failed", root);
            return new MediaFinished(MediaFinishedStatus.Incomplete, Strings.Format("Media_UsbIncomplete", ex.Message));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            env.Log.Information("Media: stopped while writing to the USB stick {Root}", root);
            return new MediaFinished(MediaFinishedStatus.Incomplete, Strings.Format("Media_UsbIncomplete", Strings.Get("Media_Interrupted")));
        }

        return new MediaFinished(MediaFinishedStatus.Done, Strings.Get("Media_UsbDone"));
    }

    // The builder logs the generated answer file in full at Information level (spec 10).
    private Task WriteAsync(PreparedMedia prepared, MediaRequest request, string target) =>
        new MediaBuilder(env.Services, env.Log).WriteAsync(prepared, request, target, new PipeProgress(channel), stop);

    private static DiskRow Row(UsbCandidate c) => new(
        c.Disk.Number, c.Disk.UniqueId, c.Disk.FriendlyName, c.Disk.SizeBytes, c.Disk.BusType, c.Disk.IsRemovable,
        c.Disk.Volumes, c.Refusal, c.SourcesOnDisk);

    /// <summary>The window has already answered: the typed text comes with the disk it chose.</summary>
    private sealed class TypedAnswer(string typed) : IDiskConfirmation
    {
        public Task<string?> AskAsync(UsbCandidate disk, CancellationToken ct) => Task.FromResult<string?>(typed);
    }

    /// <summary>Posts when the phase or the whole percent changes, not once per file. Never blocks the copy.</summary>
    private sealed class PipeProgress(MessageChannel channel) : IProgress<MediaProgress>
    {
        private MediaPhase? _phase;
        private long _percent = -1;

        public void Report(MediaProgress value)
        {
            var percent = value.TotalBytes <= 0 ? 0 : value.CopiedBytes * 100 / value.TotalBytes;
            if (_phase == value.Phase && _percent == percent)
                return;

            _phase = value.Phase;
            _percent = percent;
            channel.Post(new MediaProgressUpdate(value.Phase, value.CopiedBytes, value.TotalBytes));
        }
    }
}
