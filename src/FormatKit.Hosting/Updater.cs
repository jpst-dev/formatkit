using System.Security.Cryptography;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Updating;
using FormatKit.Hosting.Localization;
using Serilog;

namespace FormatKit.Hosting;

public enum UpdateCheckStatus
{
    UpToDate,
    Available,
    Failed,
}

/// <param name="Current">This build's version, without the commit; the raw text when it cannot be read.</param>
/// <param name="Latest">The latest release's version; null when the check failed.</param>
/// <param name="Reason">Why the check failed; null otherwise.</param>
public sealed record UpdateCheck(
    UpdateCheckStatus Status, string Current, string? Latest = null, ReleaseInfo? Release = null, string? Reason = null);

public enum UpdateStatus
{
    Updated,
    Refused,
    Failed,
}

/// <param name="Reason">Why nothing changed; null when updated.</param>
/// <param name="Files">The executables replaced.</param>
public sealed record UpdateResult(UpdateStatus Status, string? Reason, IReadOnlyList<string> Files);

/// <summary>Finds a newer release and replaces the executables next to the running one, all of them or none.</summary>
public sealed class Updater(
    IReleaseSource source, IHttpDownloader downloader, IFileSystem files, RunStateStore state,
    string exePath, string currentVersion, ILogger log)
{
    private const string SumsName = "SHA256SUMS.txt";
    private const string UpdateSuffix = ".update";
    private const string OldSuffix = ".old";
    private const string AssetPrefix = "/jpst-dev/formatkit/releases/download/";
    private static readonly string[] Executables = ["formatkit.exe", "FormatKitUI.exe"];
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

    public async Task<UpdateCheck> CheckAsync(CancellationToken ct)
    {
        if (!AppVersion.TryParse(currentVersion, out var current))
            return new UpdateCheck(UpdateCheckStatus.Failed, currentVersion, Reason: Strings.Format("Update_BadVersion", currentVersion));

        ReleaseInfo release;
        try
        {
            release = await source.GetLatestAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or InvalidDataException or IOException)
        {
            log.Information("Update check failed: {Error}", ex.Message);
            return new UpdateCheck(UpdateCheckStatus.Failed, current!.Text, Reason: ex.Message);
        }

        if (!AppVersion.TryParse(release.Tag, out var latest))
            return new UpdateCheck(UpdateCheckStatus.Failed, current!.Text, Reason: Strings.Format("Update_BadVersion", release.Tag));

        var status = latest!.CompareTo(current) > 0 ? UpdateCheckStatus.Available : UpdateCheckStatus.UpToDate;
        return new UpdateCheck(status, current!.Text, latest.Text, release);
    }

    /// <summary>Why this copy must not update now; null when it may.</summary>
    public string? Refusal()
    {
        var name = Path.GetFileName(exePath);
        if (!Executables.Contains(name, StringComparer.OrdinalIgnoreCase))
            return Strings.Format("Update_NotReleased", name);

        // The resume task starts this same path after a reboot, so a reinstall must not change program halfway.
        return state.Load().Status == StateLoadStatus.Loaded ? Strings.Get("Update_RunPending") : null;
    }

    public async Task<UpdateResult> UpdateAsync(ReleaseInfo release, CancellationToken ct)
    {
        if (Refusal() is { } refusal)
            return new UpdateResult(UpdateStatus.Refused, refusal, []);

        var folder = Path.GetDirectoryName(exePath)!;
        var present = Executables.Where(name => files.FileExists(Path.Combine(folder, name))).ToList();
        try
        {
            var assets = SelectAssets(release, present);
            var sums = await ReadSumsAsync(assets[SumsName], folder, ct).ConfigureAwait(false);
            foreach (var name in present)
                await DownloadAsync(assets[name], sums, folder, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            // A download takes minutes, and a run started meanwhile must resume on the program it started with.
            if (Refusal() is { } late)
            {
                RemoveUpdates(folder);
                return new UpdateResult(UpdateStatus.Refused, late, []);
            }

            RemoveOld(folder, present);
            Swap(folder, present);
            log.Information("Updated {Files} to {Tag}", present, release.Tag);
            return new UpdateResult(UpdateStatus.Updated, null, present);
        }
        catch (OperationCanceledException)
        {
            RemoveUpdates(folder);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TimeoutException or InvalidDataException)
        {
            log.Warning(ex, "The update failed; the current files were kept");
            RemoveUpdates(folder);
            return new UpdateResult(UpdateStatus.Failed, ex.Message, []);
        }
    }

    /// <summary>Clears what an earlier update left. A running program cannot delete itself, so this waits for the next start.</summary>
    public static void RemoveLeftovers(IFileSystem files, string exePath)
    {
        if (!Executables.Contains(Path.GetFileName(exePath), StringComparer.OrdinalIgnoreCase))
            return;

        var folder = Path.GetDirectoryName(exePath)!;
        foreach (var name in Executables.Append(SumsName))
        {
            foreach (var suffix in new[] { OldSuffix, UpdateSuffix, UpdateSuffix + ".part" })
            {
                try
                {
                    var path = Path.Combine(folder, name + suffix);
                    if (files.FileExists(path))
                        files.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Another copy may still be running; the next start tries again.
                }
            }
        }
    }

    private static Dictionary<string, ReleaseAsset> SelectAssets(ReleaseInfo release, IEnumerable<string> present)
    {
        var assets = new Dictionary<string, ReleaseAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in present.Append(SumsName))
        {
            var asset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException(Strings.Format("Update_MissingAsset", name));
            if (!IsFromThisRepository(asset.Url))
                throw new InvalidDataException(Strings.Format("Update_BadAddress", asset.Url));
            assets[name] = asset;
        }

        return assets;
    }

    private static bool IsFromThisRepository(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps
        && url.IsDefaultPort
        && string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase)
        && url.AbsolutePath.StartsWith(AssetPrefix, StringComparison.Ordinal);

    private async Task<Dictionary<string, string>> ReadSumsAsync(ReleaseAsset asset, string folder, CancellationToken ct)
    {
        var path = Path.Combine(folder, SumsName + UpdateSuffix);
        await downloader.DownloadAsync(asset.Url, path, DownloadTimeout, ct).ConfigureAwait(false);
        var text = files.ReadAllText(path);
        files.Delete(path);

        var sums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
                sums[parts[1]] = parts[0];
        }

        return sums;
    }

    private async Task DownloadAsync(ReleaseAsset asset, Dictionary<string, string> sums, string folder, CancellationToken ct)
    {
        var name = asset.Name;
        if (!sums.TryGetValue(name, out var expected))
            throw new InvalidDataException(Strings.Format("Update_MissingSum", name));

        var path = Path.Combine(folder, name + UpdateSuffix);
        log.Information("Downloading {Url}", asset.Url);
        await downloader.DownloadAsync(asset.Url, path, DownloadTimeout, ct).ConfigureAwait(false);

        await using var stream = files.OpenRead(path);
        if (stream.Length != asset.Size)
            throw new InvalidDataException(Strings.Format("Update_BadFile", name));

        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Strings.Format("Update_BadFile", name));
    }

    private void RemoveOld(string folder, IEnumerable<string> present)
    {
        foreach (var name in present)
        {
            var old = Path.Combine(folder, name + OldSuffix);
            try
            {
                if (files.FileExists(old))
                    files.Delete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(Strings.Get("Update_CloseFormatKit"), ex);
            }
        }
    }

    /// <summary>Windows lets a running executable be renamed, so nothing needs to wait for this process to end.</summary>
    private void Swap(string folder, IReadOnlyList<string> present)
    {
        var moved = new List<string>();
        try
        {
            foreach (var name in present)
            {
                var current = Path.Combine(folder, name);
                files.Move(current, current + OldSuffix);
                moved.Add(name);
                files.Move(current + UpdateSuffix, current);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Restore(folder, moved);
            throw;
        }
    }

    private void Restore(string folder, IEnumerable<string> moved)
    {
        foreach (var name in moved.Reverse())
        {
            var current = Path.Combine(folder, name);
            try
            {
                if (files.FileExists(current))
                    files.Delete(current);
                files.Move(current + OldSuffix, current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Error(ex, "Could not restore {File} from its .old copy", current);
            }
        }
    }

    private void RemoveUpdates(string folder)
    {
        foreach (var name in Executables.Append(SumsName))
        {
            try
            {
                var path = Path.Combine(folder, name + UpdateSuffix);
                if (files.FileExists(path))
                    files.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Warning(ex, "Could not remove a downloaded update file");
            }
        }
    }
}
