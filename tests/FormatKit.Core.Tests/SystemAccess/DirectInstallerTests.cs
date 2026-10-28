using System.Security.Cryptography;
using System.Text;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class DirectInstallerTests
{
    private static readonly AppPaths Paths = new(@"C:\FK");
    private const string Content = "installer-bytes";
    private static readonly string ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Content)));

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeProcessRunner _runner = new();
    private readonly FakeHttpDownloader _http;
    private readonly DirectInstaller _installer;

    public DirectInstallerTests()
    {
        _http = new FakeHttpDownloader(_fs) { Content = Content };
        _installer = new DirectInstaller(_http, _runner, _fs, Paths);
    }

    private static ProcessResult Exit(int code, bool timedOut = false) => new(code, "", "", TimeSpan.Zero, timedOut);

    private static DirectSource Url(string url, string? sha = null, params string[] args) =>
        new("App", new Uri(url), null, sha, args);

    private string DownloadedPath() => Assert.Single(_http.Calls).Destination;

    private static string ExpectedFile(string url)
    {
        var uri = new Uri(url);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.ToString())))[..16].ToLowerInvariant();
        return Path.Combine(Paths.DownloadsDirectory, hash + Path.GetExtension(uri.AbsolutePath));
    }

    [Fact]
    public async Task Url_with_matching_hash_downloads_and_runs_exe_with_args()
    {
        var url = "https://example.com/dl/setup.exe";
        _runner.Enqueue(ExpectedFile(url), Exit(0));

        var result = await _installer.InstallAsync(Url(url, ContentHash.ToLowerInvariant(), "/S", "/D=x"), CancellationToken.None);

        Assert.Equal(DirectStatus.Installed, result.Status);
        Assert.Equal(ExpectedFile(url), DownloadedPath());
        Assert.True(_fs.DirectoryExists(Paths.DownloadsDirectory));
        var call = Assert.Single(_runner.Calls);
        Assert.Equal(DownloadedPath(), call.FileName);
        Assert.Equal(["/S", "/D=x"], call.Arguments);
        Assert.Equal(Timeouts.Install, call.Timeout);
        Assert.Equal(Timeouts.Download, Assert.Single(_http.Calls).Timeout);
        Assert.False(_fs.FileExists(DownloadedPath()));
    }

    [Fact]
    public async Task Hash_mismatch_deletes_download_and_runs_nothing()
    {
        var result = await _installer.InstallAsync(Url("https://example.com/setup.exe", new string('0', 64)), CancellationToken.None);

        Assert.Equal(DirectStatus.HashMismatch, result.Status);
        Assert.Empty(_runner.Calls);
        Assert.False(_fs.FileExists(DownloadedPath()));
    }

    [Fact]
    public async Task Msi_uses_msiexec_quiet_line()
    {
        _runner.Enqueue("msiexec.exe", Exit(0));

        var result = await _installer.InstallAsync(Url("https://example.com/app.msi", ContentHash, "/ignored"), CancellationToken.None);

        Assert.Equal(DirectStatus.Installed, result.Status);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal("msiexec.exe", call.FileName);
        Assert.Equal(["/i", DownloadedPath(), "/qn", "/norestart"], call.Arguments);
    }

    [Theory]
    [InlineData(3010)]
    [InlineData(1641)]
    public async Task Exit_3010_and_1641_are_reboot_required(int code)
    {
        var url = "https://example.com/setup.exe";
        _runner.Enqueue(ExpectedFile(url), Exit(code));

        var result = await _installer.InstallAsync(Url(url), CancellationToken.None);

        Assert.Equal(DirectStatus.RebootRequired, result.Status);
        Assert.Equal(code, result.ExitCode);
        Assert.False(_fs.FileExists(DownloadedPath()));
    }

    [Fact]
    public async Task Other_exit_code_fails_and_keeps_download()
    {
        var url = "https://example.com/setup.exe";
        _runner.Enqueue(ExpectedFile(url), Exit(5));

        var result = await _installer.InstallAsync(Url(url), CancellationToken.None);

        Assert.Equal(DirectStatus.Failed, result.Status);
        Assert.Equal(5, result.ExitCode);
        Assert.Contains("5", result.Message);
        Assert.True(_fs.FileExists(DownloadedPath()));
    }

    [Fact]
    public async Task Download_error_returns_download_failed_and_runs_nothing()
    {
        _http.Throws = new HttpRequestException("404 gone");

        var result = await _installer.InstallAsync(Url("https://example.com/setup.exe"), CancellationToken.None);

        Assert.Equal(DirectStatus.DownloadFailed, result.Status);
        Assert.Equal("404 gone", result.Message);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task Download_timeout_returns_download_failed()
    {
        _http.Throws = new TimeoutException("slow");

        var result = await _installer.InstallAsync(Url("https://example.com/setup.exe"), CancellationToken.None);

        Assert.Equal(DirectStatus.DownloadFailed, result.Status);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_from_download()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _http.Throws = new OperationCanceledException(cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _installer.InstallAsync(Url("https://example.com/setup.exe"), cts.Token));
    }

    [Fact]
    public async Task Local_path_without_hash_runs_in_place_and_is_not_deleted()
    {
        var path = @"C:\Setup\tool.exe";
        _fs.Seed(path, Content);
        _runner.Enqueue(path, Exit(0));

        var result = await _installer.InstallAsync(new DirectSource("Tool", null, path, null, ["/q"]), CancellationToken.None);

        Assert.Equal(DirectStatus.Installed, result.Status);
        Assert.Empty(_http.Calls);
        Assert.Equal(path, Assert.Single(_runner.Calls).FileName);
        Assert.True(_fs.FileExists(path));
    }

    [Fact]
    public async Task Local_path_with_wrong_hash_is_not_deleted()
    {
        var path = @"C:\Setup\tool.exe";
        _fs.Seed(path, Content);

        var result = await _installer.InstallAsync(new DirectSource("Tool", null, path, new string('a', 64), []), CancellationToken.None);

        Assert.Equal(DirectStatus.HashMismatch, result.Status);
        Assert.Empty(_runner.Calls);
        Assert.True(_fs.FileExists(path));
    }

    [Fact]
    public async Task Timed_out_installer_fails()
    {
        var url = "https://example.com/setup.exe";
        _runner.Enqueue(ExpectedFile(url), Exit(-1, timedOut: true));

        var result = await _installer.InstallAsync(Url(url), CancellationToken.None);

        Assert.Equal(DirectStatus.Failed, result.Status);
        Assert.True(_fs.FileExists(DownloadedPath()));
    }

    [Fact]
    public async Task Unsupported_extension_throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _installer.InstallAsync(Url("https://example.com/setup.zip"), CancellationToken.None));
    }

    [Fact]
    public async Task HttpDownloader_rejects_http_scheme()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new HttpDownloader().DownloadAsync(
            new Uri("http://example.com/setup.exe"), Path.Combine(Path.GetTempPath(), "never.exe"),
            TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    [Fact]
    public async Task Cleanup_delete_failure_keeps_installed_status()
    {
        var url = "https://example.com/setup.exe";
        _runner.Enqueue(ExpectedFile(url), Exit(0));
        _fs.FailDeletes = true;

        var result = await _installer.InstallAsync(Url(url), CancellationToken.None);

        Assert.Equal(DirectStatus.Installed, result.Status);
    }

    [Fact]
    public async Task Cleanup_delete_failure_keeps_hash_mismatch_status()
    {
        _fs.FailDeletes = true;

        var result = await _installer.InstallAsync(Url("https://example.com/setup.exe", new string('0', 64)), CancellationToken.None);

        Assert.Equal(DirectStatus.HashMismatch, result.Status);
    }
}
