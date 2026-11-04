using System.Security.Cryptography;
using System.Text;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using Serilog.Core;

namespace FormatKit.Hosting.Tests;

public sealed class UpdaterTests
{
    private const string Dir = @"C:\Tools";
    private const string Cli = @"C:\Tools\formatkit.exe";
    private const string Ui = @"C:\Tools\FormatKitUI.exe";
    private const string Download = "https://github.com/jpst-dev/formatkit/releases/download/v0.3.0/";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryFileSystem _files = new();
    private readonly FakeHttpDownloader _downloader;
    private readonly FakeSource _source = new();
    private readonly AppPaths _paths = new(@"C:\ProgramData\FormatKit");

    private sealed class FakeSource : IReleaseSource
    {
        public ReleaseInfo? Release { get; set; }

        public Exception? Throws { get; set; }

        public int Calls { get; private set; }

        public Task<ReleaseInfo> GetLatestAsync(CancellationToken ct)
        {
            Calls++;
            return Throws is not null ? Task.FromException<ReleaseInfo>(Throws) : Task.FromResult(Release!);
        }
    }

    public UpdaterTests()
    {
        _downloader = new FakeHttpDownloader(_files) { ContentFor = url => Contents[url.Segments[^1]] };
        _files.Seed(Cli, "old cli");
        _files.Seed(Ui, "old ui");
        _source.Release = Release();
    }

    private Dictionary<string, string> Contents { get; } = new()
    {
        ["formatkit.exe"] = "new cli",
        ["FormatKitUI.exe"] = "new ui",
        ["SHA256SUMS.txt"] = Sums(("new cli", "formatkit.exe"), ("new ui", "FormatKitUI.exe")),
    };

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string Sums(params (string Content, string Name)[] entries) =>
        string.Join('\n', entries.Select(e => $"{Hash(e.Content)}  {e.Name}")) + "\n";

    private static ReleaseAsset Asset(string name, long? size = null) =>
        new(name, new Uri(Download + name), size ?? name switch { "formatkit.exe" => 7, "FormatKitUI.exe" => 6, _ => 1 });

    private static ReleaseInfo Release(params ReleaseAsset[] assets) => new(
        "v0.3.0",
        new Uri("https://github.com/jpst-dev/formatkit/releases/tag/v0.3.0"),
        assets.Length > 0 ? assets : [Asset("formatkit.exe"), Asset("FormatKitUI.exe"), Asset("SHA256SUMS.txt")]);

    private Updater Create(string exe = Cli, string version = "0.2.1+abc") =>
        new(_source, _downloader, _files, new RunStateStore(_files, _paths), exe, version, Logger.None);

    private void AssertOriginalsKept()
    {
        Assert.Equal("old cli", _files.ReadAllText(Cli));
        Assert.Equal("old ui", _files.ReadAllText(Ui));
        Assert.DoesNotContain(_files.Files.Keys, k => k.EndsWith(".update", StringComparison.Ordinal));
        Assert.DoesNotContain(_files.Files.Keys, k => k.EndsWith(".old", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0.2.1+abc", UpdateCheckStatus.Available)]
    [InlineData("0.3.0-rc.1", UpdateCheckStatus.Available)]
    [InlineData("0.3.0+abc", UpdateCheckStatus.UpToDate)]
    [InlineData("0.4.0", UpdateCheckStatus.UpToDate)]
    public async Task Check_compares_with_the_latest_release_and_never_offers_a_downgrade(string version, UpdateCheckStatus expected)
    {
        var check = await Create(version: version).CheckAsync(Ct);

        Assert.Equal(expected, check.Status);
    }

    [Fact]
    public async Task Check_reports_the_versions_without_the_commit()
    {
        var check = await Create().CheckAsync(Ct);

        Assert.Equal("0.2.1", check.Current);
        Assert.Equal("0.3.0", check.Latest);
        Assert.Same(_source.Release, check.Release);
    }

    [Theory]
    [MemberData(nameof(CheckFailures))]
    public async Task Check_failure_gives_a_reason_and_does_not_throw(Exception error)
    {
        _source.Throws = error;

        var check = await Create().CheckAsync(Ct);

        Assert.Equal(UpdateCheckStatus.Failed, check.Status);
        Assert.False(string.IsNullOrEmpty(check.Reason));
    }

    public static TheoryData<Exception> CheckFailures =>
    [
        new HttpRequestException("offline"),
        new TimeoutException("slow"),
        new InvalidDataException("odd"),
    ];

    [Fact]
    public async Task Check_cancelled_is_not_a_failure()
    {
        _source.Throws = new OperationCanceledException(Ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().CheckAsync(Ct));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("nope")]
    public async Task Check_with_an_unreadable_release_tag_fails(string tag)
    {
        _source.Release = _source.Release! with { Tag = tag };

        Assert.Equal(UpdateCheckStatus.Failed, (await Create().CheckAsync(Ct)).Status);
    }

    [Fact]
    public async Task Check_with_an_unreadable_own_version_fails()
    {
        var check = await Create(version: "dev").CheckAsync(Ct);

        Assert.Equal(UpdateCheckStatus.Failed, check.Status);
        Assert.Equal(0, _source.Calls);
    }

    [Theory]
    [InlineData(@"C:\Tools\dotnet.exe")]
    [InlineData(@"C:\Tools\FormatKit.Cli.exe")]
    public void Refuses_a_file_that_is_not_a_released_executable(string exe) =>
        Assert.NotNull(Create(exe).Refusal());

    [Theory]
    [InlineData(Cli)]
    [InlineData(Ui)]
    [InlineData(@"C:\Tools\FORMATKIT.EXE")]
    public void Released_executable_is_not_refused(string exe) =>
        Assert.Null(Create(exe).Refusal());

    [Fact]
    public async Task A_saved_run_state_refuses_the_update_and_changes_nothing()
    {
        new RunStateStore(_files, _paths).Save(new RunState(@"C:\p.json", "hash", ["a"], [], DateTimeOffset.UnixEpoch));

        var updater = Create();
        Assert.NotNull(updater.Refusal());

        var result = await updater.UpdateAsync(_source.Release!, Ct);

        Assert.Equal(UpdateStatus.Refused, result.Status);
        Assert.Empty(_downloader.Calls);
        AssertOriginalsKept();
    }

    [Fact]
    public async Task Update_replaces_both_executables_and_keeps_the_old_files_for_the_next_start()
    {
        var result = await Create().UpdateAsync(_source.Release!, Ct);

        Assert.Equal(UpdateStatus.Updated, result.Status);
        Assert.Equal("new cli", _files.ReadAllText(Cli));
        Assert.Equal("new ui", _files.ReadAllText(Ui));
        Assert.Equal("old cli", _files.ReadAllText(Cli + ".old"));
        Assert.Equal("old ui", _files.ReadAllText(Ui + ".old"));
        Assert.DoesNotContain(_files.Files.Keys, k => k.EndsWith(".update", StringComparison.Ordinal));
        Assert.Equal(["formatkit.exe", "FormatKitUI.exe"], result.Files);
    }

    [Fact]
    public async Task Update_does_not_add_an_executable_the_user_does_not_keep()
    {
        _files.Delete(Ui);

        var result = await Create().UpdateAsync(_source.Release!, Ct);

        Assert.Equal(UpdateStatus.Updated, result.Status);
        Assert.False(_files.FileExists(Ui));
        Assert.DoesNotContain(_downloader.Calls, c => c.Url.AbsoluteUri.EndsWith("FormatKitUI.exe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_does_not_need_an_asset_for_an_executable_that_is_absent()
    {
        _files.Delete(Ui);

        var result = await Create().UpdateAsync(Release(Asset("formatkit.exe"), Asset("SHA256SUMS.txt")), Ct);

        Assert.Equal(UpdateStatus.Updated, result.Status);
    }

    [Fact]
    public async Task Update_works_from_the_window_too()
    {
        var result = await Create(Ui).UpdateAsync(_source.Release!, Ct);

        Assert.Equal(UpdateStatus.Updated, result.Status);
        Assert.Equal("new cli", _files.ReadAllText(Cli));
    }

    [Fact]
    public async Task Missing_sums_fail_before_any_download()
    {
        var result = await Create().UpdateAsync(Release(Asset("formatkit.exe"), Asset("FormatKitUI.exe")), Ct);

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Empty(_downloader.Calls);
        AssertOriginalsKept();
    }

    [Fact]
    public async Task Missing_executable_asset_fails_before_any_download()
    {
        var result = await Create().UpdateAsync(Release(Asset("formatkit.exe"), Asset("SHA256SUMS.txt")), Ct);

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Empty(_downloader.Calls);
        AssertOriginalsKept();
    }

    [Theory]
    [InlineData("https://example.com/jpst-dev/formatkit/releases/download/v0.3.0/formatkit.exe")]
    [InlineData("http://github.com/jpst-dev/formatkit/releases/download/v0.3.0/formatkit.exe")]
    [InlineData("https://github.com/someone/formatkit/releases/download/v0.3.0/formatkit.exe")]
    [InlineData("https://github.com.evil.example/jpst-dev/formatkit/releases/download/v0.3.0/formatkit.exe")]
    [InlineData("https://github.com/jpst-dev/formatkit/releases/download/../../../x/formatkit.exe")]
    public async Task An_address_outside_the_repository_is_refused(string url)
    {
        var bad = new ReleaseAsset("formatkit.exe", new Uri(url), 7);

        var result = await Create().UpdateAsync(Release(bad, Asset("FormatKitUI.exe"), Asset("SHA256SUMS.txt")), Ct);

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Empty(_downloader.Calls);
        AssertOriginalsKept();
    }

    [Fact]
    public async Task A_failed_download_leaves_the_originals()
    {
        _downloader.Throws = new HttpRequestException("offline");

        var result = await Create().UpdateAsync(_source.Release!, Ct);

        Assert.Equal(UpdateStatus.Failed, result.Status);
        AssertOriginalsKept();
    }

    [Fact]
    public async Task A_wrong_hash_leaves_the_originals()
    {
        Contents["FormatKitUI.exe"] = "bad ui";

        var result = await Create().UpdateAsync(_source.Release!, Ct);

        Assert.Equal(UpdateStatus.Failed, result.Status);
        AssertOriginalsKept();
    }

    [Fact]
    public async Task A_wrong_size_leaves_the_originals()
    {
        var result = await Create().UpdateAsync(Release(Asset("formatkit.exe", 99), Asset("FormatKitUI.exe"), Asset("SHA256SUMS.txt")), Ct);

        Assert.Equal(UpdateStatus.Failed, result.Status);
        AssertOriginalsKept();
    }

    [Fact]
    public async Task An_executable_missing_from_the_sums_leaves_the_originals()
    {
        Contents["SHA256SUMS.txt"] = Sums(("new cli", "formatkit.exe"));

        var result = await Create().UpdateAsync(_source.Release!, Ct);

        Assert.Equal(UpdateStatus.Failed, result.Status);
        AssertOriginalsKept();
    }

    [Fact]
    public async Task A_locked_old_file_stops_before_anything_is_replaced()
    {
        _files.Seed(Cli + ".old", "older");
        _files.FailDeletes = true;

        var result = await Create().UpdateAsync(_source.Release!, Ct);

        _files.FailDeletes = false;
        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Equal("old cli", _files.ReadAllText(Cli));
        Assert.Equal("old ui", _files.ReadAllText(Ui));
    }

    [Fact]
    public async Task An_old_file_left_by_an_earlier_update_is_replaced()
    {
        _files.Seed(Cli + ".old", "older");

        var result = await Create().UpdateAsync(_source.Release!, Ct);

        Assert.Equal(UpdateStatus.Updated, result.Status);
        Assert.Equal("old cli", _files.ReadAllText(Cli + ".old"));
    }

    [Theory]
    [InlineData(@"C:\Tools\FormatKitUI.exe", @"C:\Tools\FormatKitUI.exe.old")] // Second file, first step.
    [InlineData(@"C:\Tools\FormatKitUI.exe.update", @"C:\Tools\FormatKitUI.exe")] // Second file, second step.
    [InlineData(@"C:\Tools\formatkit.exe.update", @"C:\Tools\formatkit.exe")] // First file, second step.
    public async Task A_failed_swap_restores_every_original(string source, string destination)
    {
        _files.FailMove = (from, to) => from == source && to == destination;

        var result = await Create().UpdateAsync(_source.Release!, Ct);

        _files.FailMove = null;
        Assert.Equal(UpdateStatus.Failed, result.Status);
        AssertOriginalsKept();
    }

    [Fact]
    public async Task Cancelling_during_the_downloads_leaves_the_originals()
    {
        using var cts = new CancellationTokenSource();
        _downloader.Throws = new OperationCanceledException(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().UpdateAsync(_source.Release!, cts.Token));

        AssertOriginalsKept();
    }

    [Fact]
    public void RemoveLeftovers_deletes_only_this_tools_old_and_update_files()
    {
        _files.Seed(Cli + ".old", "x");
        _files.Seed(Ui + ".old", "x");
        _files.Seed(Ui + ".update", "x");
        _files.Seed(Ui + ".update.part", "x");
        _files.Seed(Dir + @"\SHA256SUMS.txt.update", "x");
        _files.Seed(Dir + @"\notes.old", "x");

        Updater.RemoveLeftovers(_files, Cli);

        Assert.Equal(new[] { Cli, Ui, Dir + @"\notes.old" }.Order(), _files.Files.Keys.Order());
    }

    [Fact]
    public void RemoveLeftovers_ignores_a_locked_file()
    {
        _files.Seed(Cli + ".old", "x");
        _files.FailDeletes = true;

        Updater.RemoveLeftovers(_files, Cli);

        _files.FailDeletes = false;
        Assert.True(_files.FileExists(Cli + ".old"));
    }

    [Fact]
    public void RemoveLeftovers_leaves_another_program_alone()
    {
        _files.Seed(@"C:\Other\app.exe.old", "x");

        Updater.RemoveLeftovers(_files, @"C:\Other\app.exe");

        Assert.True(_files.FileExists(@"C:\Other\app.exe.old"));
    }
}
