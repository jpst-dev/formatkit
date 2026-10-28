using System.Security.Cryptography;
using System.Text;

namespace FormatKit.Core.SystemAccess;

public enum DirectStatus { Installed, RebootRequired, HashMismatch, DownloadFailed, Failed }

public sealed record DirectResult(DirectStatus Status, int ExitCode, string Message);

public sealed record DirectSource(string Name, Uri? Url, string? LocalPath, string? Sha256, IReadOnlyList<string> Args);

public class DirectInstaller(IHttpDownloader downloader, IProcessRunner runner, IFileSystem files, AppPaths paths)
{
    public async Task<DirectResult> InstallAsync(DirectSource source, CancellationToken ct)
    {
        string file;
        bool downloaded;
        if (source.Url is not null)
        {
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

        if (source.Sha256 is not null)
        {
            var actual = ComputeSha256(file);
            if (!string.Equals(actual, source.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                if (downloaded)
                    files.Delete(file);
                return new DirectResult(DirectStatus.HashMismatch, -1,
                    $"SHA-256 mismatch for '{source.Name}': expected {source.Sha256}, got {actual}.");
            }
        }

        var spec = extension == ".msi"
            ? new ProcessSpec("msiexec.exe", ["/i", file, "/qn", "/norestart"], Timeouts.Install)
            : new ProcessSpec(file, source.Args, Timeouts.Install);

        var result = await runner.RunAsync(spec, ct).ConfigureAwait(false);

        if (result.TimedOut)
            return new DirectResult(DirectStatus.Failed, result.ExitCode, $"Installer for '{source.Name}' timed out.");

        switch (result.ExitCode)
        {
            case 0:
                if (downloaded)
                    files.Delete(file);
                return new DirectResult(DirectStatus.Installed, 0, "Installed.");
            case 3010:
            case 1641:
                if (downloaded)
                    files.Delete(file);
                return new DirectResult(DirectStatus.RebootRequired, result.ExitCode, "Installed; a reboot is required.");
            default:
                return new DirectResult(DirectStatus.Failed, result.ExitCode,
                    $"Installer for '{source.Name}' exited with code {result.ExitCode}.");
        }
    }

    private string ComputeSha256(string path)
    {
        using var stream = files.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string UrlHash(Uri url) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url.ToString())))[..16].ToLowerInvariant();
}
