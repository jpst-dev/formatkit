using System.Text;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Media.Localization;
using FormatKit.Media.Setup;
using FormatKit.Media.Unattend;
using Serilog;

namespace FormatKit.Media.Building;

/// <summary>Mounts and validates an ISO, then writes its tree, the FormatKit bundle and the answer file to a folder.</summary>
public sealed class MediaBuilder(ServiceSet services, ILogger log)
{
    /// <summary>The largest file FAT32 holds: 4 GiB minus 1 byte.</summary>
    public const long Fat32MaxFile = 4294967295;

    public const int SplitPartMb = 3800;

    private readonly IFileSystem _files = services.Files;

    public async Task<PreparedMedia> PrepareAsync(MediaRequest request, CancellationToken ct)
    {
        string root;
        try
        {
            root = await services.DiskImages.MountAsync(request.IsoPath, ct);
        }
        catch (InvalidOperationException e)
        {
            throw new MediaException(Strings.Format("Media_MountFailed", e.Message));
        }

        try
        {
            var image = await InstallMedia.InspectAsync(root, services, ct);

            var names = image.Editions.Select(e => e.Name).ToList();
            var edition = request.Setup.Edition is null
                ? image.Editions[0]
                : image.Editions.FirstOrDefault(e => string.Equals(e.Name, request.Setup.Edition, StringComparison.OrdinalIgnoreCase))
                    ?? throw new MediaException(Strings.Format("Media_EditionNotInImage", "setup.edition", request.Setup.Edition, string.Join(", ", names)));

            // Spec 5.3: the edition must also have a generic key, or the answer file cannot select it.
            if (request.Setup.Edition is not null && GenericKeys.For(edition.Name) is null)
            {
                var supported = names.Where(n => GenericKeys.For(n) is not null);
                throw new MediaException(Strings.Format("Media_EditionHasNoGenericKey", "setup.edition", edition.Name, string.Join(", ", supported)));
            }

            // /IMAGE/NAME gets the image's own spelling of the name, not the profile's casing.
            var options = request.Setup.Edition is null ? request.Setup : request.Setup with { Edition = edition.Name };
            var (setup, error) = SetupDefaults.Resolve(options, request.Machine, edition);
            if (setup is null)
                throw new MediaException(error!);

            if (image.IsEsd && image.Bytes > Fat32MaxFile)
                throw new MediaException(Strings.Get("Media_EsdTooLarge"));

            var total = TreeBytes(root);
            if (request.Bundle is not null)
                total += request.Bundle.TotalBytes + FileLength(request.ExePath);
            var required = total + (total + 99) / 100;

            return new PreparedMedia(root, image, setup, required) { Dismount = () => DismountAsync(request.IsoPath) };
        }
        catch
        {
            await DismountAsync(request.IsoPath);
            throw;
        }
    }

    /// <summary>Writes the media. Any failure other than cancellation is a <see cref="MediaWriteException"/> (the target is partly written).</summary>
    public async Task WriteAsync(PreparedMedia media, MediaRequest request, string targetRoot,
        IProgress<MediaProgress>? progress, CancellationToken ct)
    {
        try
        {
            await WriteCoreAsync(media, request, targetRoot, progress, ct);
        }
        catch (Exception e) when (e is not (OperationCanceledException or MediaWriteException))
        {
            throw new MediaWriteException(e.Message, e);
        }
    }

    private async Task WriteCoreAsync(PreparedMedia media, MediaRequest request, string targetRoot,
        IProgress<MediaProgress>? progress, CancellationToken ct)
    {
        var bundleBytes = request.Bundle?.Items.Sum(i => i.Bytes) ?? 0;
        var total = TreeBytes(media.MountRoot) + bundleBytes;
        long copied = 0;
        void Report(MediaPhase phase) => progress?.Report(new MediaProgress(phase, copied, total));

        var split = !media.Image.IsEsd && media.Image.Bytes > Fat32MaxFile;
        var sources = Path.Combine(targetRoot, "sources");

        Report(MediaPhase.Copying);
        _files.CreateDirectory(targetRoot);
        foreach (var entry in _files.EnumerateFileEntries(media.MountRoot, recursive: true, Unreadable))
        {
            ct.ThrowIfCancellationRequested();

            // The chosen install image is copied as is (or split below); a second, unused one is left out.
            var isChosen = string.Equals(entry.Path, media.Image.Path, StringComparison.OrdinalIgnoreCase);
            if (IsInstallImage(entry.Path, media.MountRoot) && (!isChosen || split))
                continue;

            var destination = Path.Combine(targetRoot, Path.GetRelativePath(media.MountRoot, entry.Path));
            _files.CreateDirectory(Path.GetDirectoryName(destination)!);
            _files.Copy(entry.Path, destination);
            copied += entry.Length;
            Report(MediaPhase.Copying);
        }

        if (split)
        {
            ct.ThrowIfCancellationRequested();
            Report(MediaPhase.Splitting);
            _files.CreateDirectory(sources);
            try
            {
                await services.WindowsImages.SplitAsync(media.Image.Path, Path.Combine(sources, "install.swm"), SplitPartMb, ct);
            }
            catch (InvalidOperationException e)
            {
                throw new MediaWriteException(Strings.Format("Media_SplitFailed", e.Message), e);
            }

            copied += media.Image.Bytes;
        }

        var withProfile = request.Bundle is not null;
        if (request.Bundle is { } bundle)
        {
            ct.ThrowIfCancellationRequested();
            Report(MediaPhase.WritingFormatKit);
            var baseCopied = copied;
            bundle.Write(Path.Combine(sources, "$OEM$", "$1", "FormatKit"), request.ExePath, _files,
                new SyncProgress(done =>
                {
                    copied = baseCopied + done;
                    Report(MediaPhase.WritingFormatKit);
                }), ct);
            copied = baseCopied + bundleBytes;
        }

        ct.ThrowIfCancellationRequested();
        Report(MediaPhase.WritingAnswerFile);
        var xml = UnattendBuilder.Build(media.Setup, withProfile);
        log.Information("autounattend.xml:\n{Xml}", xml);
        _files.WriteAllBytes(Path.Combine(targetRoot, "autounattend.xml"), new UTF8Encoding(false).GetBytes(xml));

        copied = total;
        Report(MediaPhase.Done);
    }

    private static bool IsInstallImage(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return string.Equals(Path.GetDirectoryName(relative), "sources", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(relative).ToLowerInvariant() is "install.wim" or "install.esd" or "install.swm";
    }

    private long TreeBytes(string root) =>
        _files.EnumerateFileEntries(root, recursive: true, Unreadable).Sum(e => e.Length);

    private static void Unreadable(string dir) =>
        throw new MediaException(Strings.Format("Media_UnreadableFolder", dir));

    private long FileLength(string path) =>
        _files.EnumerateFileEntries(Path.GetDirectoryName(path)!, recursive: false)
            .Where(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Length)
            .FirstOrDefault();

    private async Task DismountAsync(string isoPath)
    {
        try
        {
            await services.DiskImages.DismountAsync(isoPath, CancellationToken.None);
        }
        catch (Exception e)
        {
            log.Error(e, "Could not dismount {Iso}", isoPath);
            services.Console.WriteLine(Strings.Format("Media_DismountFailed", e.Message, PowerShellText.SingleQuote(isoPath)));
        }
    }

    private sealed class SyncProgress(Action<long> onReport) : IProgress<long>
    {
        public void Report(long value) => onReport(value);
    }
}
