using System.Security.Cryptography;
using System.Text;

namespace FormatKit.Core.SystemAccess;

public enum DirectStatus { Installed, RebootRequired, HashMismatch, DownloadFailed, Failed }

/// <summary><paramref name="TimedOut"/> is true when the installer ran past its timeout (status stays Failed).</summary>
public sealed record DirectResult(DirectStatus Status, int ExitCode, string Message, bool TimedOut = false);

public sealed record DirectSource(string Name, Uri? Url, string? LocalPath, string? Sha256, IReadOnlyList<string> Args);

public class DirectInstaller(IHttpDownloader downloader, IProcessRunner runner, IFileSystem files, AppPaths paths)
{
    /// <summary>
    /// Downloads (cancellable with <paramref name="ct"/>), verifies and runs one installer. Once the installer starts
    /// it is never killed by <paramref name="ct"/>: only its own timeout ends it (spec 6).
    /// </summary>
    public async Task<DirectResult> InstallAsync(DirectSource source, CancellationToken ct)
    {
        string file;
        bool downloaded;
        if (source.Url is not null)
        {
            if (source.Sha256 is null)
                throw new ArgumentException($"'{source.Name}' is downloaded from a URL but has no SHA-256.", nameof(source));
            file = Path.Combine(paths.DownloadsDirectory, UrlHash(source.Url) + Path.GetExtension(source.Url.AbsolutePath));
            downloaded = true;
        }
        else if (source.LocalPath is not null)
        {
            file = source.LocalPath;
            downloaded = false;
        }
        else
        {
            throw new ArgumentException($"'{source.Name}' has neither a URL nor a local path.", nameof(source));
        }

        var extension = Path.GetExtension(file).ToLowerInvariant();
        if (extension is not (".exe" or ".msi"))
            throw new ArgumentException($"Unsupported installer extension '{extension}' for '{source.Name}'.", nameof(source));

        if (downloaded)
        {
            try
            {
                files.CreateDirectory(paths.DownloadsDirectory);
                await downloader.DownloadAsync(source.Url!, file, Timeouts.Download, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return new DirectResult(DirectStatus.DownloadFailed, -1, ex.Message);
            }
        }

        ProcessResult result;

        // The file stays open (read access, read sharing only) from the hash check until the installer exits, so
        // nobody can replace or delete it in between; the installer itself only needs to read it.
        await using (var hold = files.OpenRead(file))
        {
            if (source.Sha256 is not null)
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(hold, ct).ConfigureAwait(false));
                if (!string.Equals(actual, source.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    await hold.DisposeAsync().ConfigureAwait(false);
                    if (downloaded)
                        TryDelete(file);
                    return new DirectResult(DirectStatus.HashMismatch, -1,
                        $"SHA-256 mismatch for '{source.Name}': expected {source.Sha256}, got {actual}.");
                }
            }

            var spec = extension == ".msi"
                ? new ProcessSpec("msiexec.exe", ["/i", file, "/qn", "/norestart"], Timeouts.Install)
                : new ProcessSpec(file, source.Args, Timeouts.Install);

            result = await runner.RunAsync(spec, CancellationToken.None).ConfigureAwait(false);
        }

        if (result.TimedOut)
        {
            return new DirectResult(DirectStatus.Failed, result.ExitCode,
                $"Installer for '{source.Name}' timed out after {Timeouts.Install}.", TimedOut: true);
        }

        switch (result.ExitCode)
        {
            case 0:
                if (downloaded)
                    TryDelete(file);
                return new DirectResult(DirectStatus.Installed, 0, "Installed.");
            case 3010:
            case 1641:
                if (downloaded)
                    TryDelete(file);
                return new DirectResult(DirectStatus.RebootRequired, result.ExitCode, "Installed; a reboot is required.");
            default:
                return new DirectResult(DirectStatus.Failed, result.ExitCode,
                    $"Installer for '{source.Name}' exited with code {result.ExitCode} (0x{result.ExitCode:X8}).");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            files.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string UrlHash(Uri url) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url.ToString())))[..16].ToLowerInvariant();
}
