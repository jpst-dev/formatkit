using System.Security.Cryptography;
using System.Text;
using FormatKit.App.Services;
using FormatKit.App.Tests.Fakes;
using FormatKit.App.ViewModels;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Hosting;
using Serilog.Core;

namespace FormatKit.App.Tests;

public sealed class UpdateViewModelTests
{
    private const string Exe = @"C:\Tools\FormatKitUI.exe";
    private const string Download = "https://github.com/jpst-dev/formatkit/releases/download/v0.3.0/";
    private const string Settings = @"C:\local\app.json";

    private readonly InMemoryFileSystem _files = new();
    private readonly FakeSource _source = new();
    private readonly FakeHttpDownloader _downloader;
    private readonly FakeDialogService _dialogs = new();
    private IHttpDownloader? _otherDownloader;
    private readonly RunStateStore _state;

    private sealed class FakeSource : IReleaseSource
    {
        public Exception? Throws { get; set; }

        public string Tag { get; set; } = "v0.3.0";

        public int Calls { get; private set; }

        public Task<ReleaseInfo> GetLatestAsync(CancellationToken ct)
        {
            Calls++;
            return Throws is not null
                ? Task.FromException<ReleaseInfo>(Throws)
                : Task.FromResult(new ReleaseInfo(
                    Tag,
                    new Uri("https://github.com/jpst-dev/formatkit/releases/tag/v0.3.0"),
                    [
                        new ReleaseAsset("FormatKitUI.exe", new Uri(Download + "FormatKitUI.exe"), 3),
                        new ReleaseAsset("SHA256SUMS.txt", new Uri(Download + "SHA256SUMS.txt"), 1),
                    ]));
        }
    }

    public UpdateViewModelTests()
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("new"))).ToLowerInvariant();
        _downloader = new FakeHttpDownloader(_files)
        {
            ContentFor = url => url.Segments[^1] == "SHA256SUMS.txt" ? $"{hash}  FormatKitUI.exe\n" : "new",
        };
        _files.Seed(Exe, "old");
        _state = new RunStateStore(_files, new AppPaths(@"C:\data\FormatKit"));
    }

    private UpdateViewModel Create(string exe = Exe, string version = "0.2.1") => new(
        new Updater(_source, _otherDownloader ?? _downloader, _files, _state, exe, version, Logger.None),
        new AppSettings(_files, Settings),
        _dialogs);

    /// <summary>Stays on the download until the update is cancelled.</summary>
    private sealed class StuckDownloader : IHttpDownloader
    {
        public Task DownloadAsync(Uri url, string destination, TimeSpan timeout, CancellationToken ct) =>
            Task.Delay(Timeout.Infinite, ct);
    }

    private void SaveRunState() =>
        _state.Save(new RunState("p.json", "hash", ["a"], [], DateTimeOffset.UnixEpoch));

    [Fact]
    public async Task Start_check_offers_a_newer_version()
    {
        var update = Create();

        await update.CheckOnStartAsync();

        Assert.True(update.IsAvailable);
        Assert.Equal("0.3.0", update.Latest);
        Assert.Equal("https://github.com/jpst-dev/formatkit/releases/tag/v0.3.0", update.ReleasePage!.AbsoluteUri);
    }

    [Fact]
    public async Task Start_check_stays_quiet_when_it_fails()
    {
        _source.Throws = new HttpRequestException("offline");
        var update = Create();

        await update.CheckOnStartAsync();

        Assert.False(update.IsAvailable);
        Assert.Null(update.Status);
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task Start_check_stays_quiet_when_up_to_date()
    {
        _source.Tag = "v0.2.1";
        var update = Create();

        await update.CheckOnStartAsync();

        Assert.False(update.IsAvailable);
        Assert.Null(update.Status);
    }

    [Fact]
    public async Task Start_check_respects_the_setting()
    {
        var update = Create();
        update.CheckForUpdates = false;

        await update.CheckOnStartAsync();

        Assert.Equal(0, _source.Calls);
    }

    [Fact]
    public async Task Start_check_is_skipped_with_a_saved_run_state()
    {
        SaveRunState();

        await Create().CheckOnStartAsync();

        Assert.Equal(0, _source.Calls);
    }

    [Fact]
    public async Task Start_check_is_skipped_in_a_development_build()
    {
        await Create(@"C:\dev\FormatKit.App.exe").CheckOnStartAsync();

        Assert.Equal(0, _source.Calls);
    }

    [Fact]
    public async Task The_button_reports_up_to_date()
    {
        _source.Tag = "v0.2.1";
        var update = Create();

        await update.CheckCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(update.Status));
        Assert.False(update.IsAvailable);
    }

    [Fact]
    public async Task The_button_reports_a_failure_with_its_reason()
    {
        _source.Throws = new HttpRequestException("offline");
        var update = Create();

        await update.CheckCommand.ExecuteAsync(null);

        Assert.Contains("offline", update.Status);
    }

    [Fact]
    public async Task The_button_works_even_with_the_setting_off()
    {
        var update = Create();
        update.CheckForUpdates = false;

        await update.CheckCommand.ExecuteAsync(null);

        Assert.True(update.IsAvailable);
    }

    [Fact]
    public async Task Update_replaces_the_file_and_asks_to_reopen()
    {
        var update = Create();
        var restarts = 0;
        update.RestartRequested += () => restarts++;
        await update.CheckOnStartAsync();

        await update.UpdateCommand.ExecuteAsync(null);

        Assert.Equal("new", _files.ReadAllText(Exe));
        Assert.Equal(1, restarts);
    }

    [Fact]
    public async Task A_failed_update_shows_the_reason_and_keeps_the_offer()
    {
        var update = Create();
        var restarts = 0;
        update.RestartRequested += () => restarts++;
        await update.CheckOnStartAsync();
        _downloader.Throws = new HttpRequestException("offline");

        await update.UpdateCommand.ExecuteAsync(null);

        Assert.Equal("old", _files.ReadAllText(Exe));
        Assert.Contains("offline", update.Status);
        Assert.True(update.IsAvailable);
        Assert.False(update.IsUpdating);
        Assert.Equal(0, restarts);
    }

    [Fact]
    public async Task Update_waits_while_other_work_runs()
    {
        var update = Create();
        update.WorkInProgress = () => true;
        await update.CheckOnStartAsync();

        await update.UpdateCommand.ExecuteAsync(null);

        Assert.Equal("old", _files.ReadAllText(Exe));
        Assert.Empty(_downloader.Calls);
        Assert.False(string.IsNullOrEmpty(update.Status));
    }

    [Fact]
    public async Task Update_is_refused_when_a_run_was_saved_after_the_check()
    {
        var update = Create();
        await update.CheckOnStartAsync();
        SaveRunState();

        await update.UpdateCommand.ExecuteAsync(null);

        Assert.Equal("old", _files.ReadAllText(Exe));
        Assert.False(string.IsNullOrEmpty(update.Status));
    }

    [Fact]
    public async Task IsUpdating_is_set_only_during_the_update()
    {
        var update = Create();
        var seen = new List<bool>();
        update.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpdateViewModel.IsUpdating))
                seen.Add(update.IsUpdating);
        };
        await update.CheckOnStartAsync();

        await update.UpdateCommand.ExecuteAsync(null);

        Assert.Equal([true, false], seen);
    }

    [Fact]
    public async Task Closing_during_an_update_can_be_declined()
    {
        _otherDownloader = new StuckDownloader();
        var update = Create();
        await update.CheckOnStartAsync();
        _dialogs.ConfirmResult = false;
        var running = update.UpdateCommand.ExecuteAsync(null);

        Assert.False(await update.ConfirmCloseAsync());

        Assert.True(update.IsUpdating);
        Assert.False(running.IsCompleted);
        _dialogs.ConfirmResult = true;
        await update.ConfirmCloseAsync(); // Lets the update end with the test.
    }

    [Fact]
    public async Task Closing_during_an_update_stops_it_and_keeps_the_files()
    {
        _otherDownloader = new StuckDownloader();
        var update = Create();
        await update.CheckOnStartAsync();
        var restarts = 0;
        update.RestartRequested += () => restarts++;
        _dialogs.ConfirmResult = true;
        var running = update.UpdateCommand.ExecuteAsync(null);

        Assert.True(await update.ConfirmCloseAsync());

        Assert.True(running.IsCompleted);
        Assert.False(update.IsUpdating);
        Assert.Equal("old", _files.ReadAllText(Exe));
        Assert.Equal(0, restarts);
    }

    [Fact]
    public void The_setting_is_saved_and_keeps_the_last_profile()
    {
        var settings = new AppSettings(_files, Settings) { LastProfile = @"C:\p.json" };
        Assert.True(settings.CheckForUpdates);

        settings.CheckForUpdates = false;

        var again = new AppSettings(_files, Settings);
        Assert.False(again.CheckForUpdates);
        Assert.Equal(@"C:\p.json", again.LastProfile);
    }

    [Fact]
    public void Saving_the_last_profile_keeps_the_setting()
    {
        var settings = new AppSettings(_files, Settings) { CheckForUpdates = false };

        settings.LastProfile = @"C:\p.json";

        Assert.False(new AppSettings(_files, Settings).CheckForUpdates);
    }

    [Fact]
    public async Task Home_banner_shows_an_offer_and_yields_to_an_interrupted_run()
    {
        var update = Create();
        await update.CheckOnStartAsync();
        var home = new HomeViewModel(_state) { Update = update };
        Assert.True(home.ShowUpdateBanner);

        SaveRunState();
        home.Refresh();

        Assert.False(home.ShowUpdateBanner);
    }

    [Fact]
    public async Task Home_banner_appears_when_the_check_finishes_after_the_page_opened()
    {
        var update = Create();
        var home = new HomeViewModel(_state) { Update = update };
        Assert.False(home.ShowUpdateBanner);
        var changed = 0;
        home.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HomeViewModel.ShowUpdateBanner))
                changed++;
        };

        await update.CheckOnStartAsync();

        Assert.True(home.ShowUpdateBanner);
        Assert.True(changed > 0);
    }

    [Fact]
    public async Task An_update_blocks_runs_and_media_and_asks_before_closing()
    {
        var update = Create();
        var main = TestViewModels.CreateMain(_files, update);
        var seen = new List<(bool Run, bool Media, bool Work)>();
        update.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpdateViewModel.IsUpdating))
                seen.Add((main.RunProfile.OtherWorkRunning, main.Media.OtherWorkRunning, main.IsWorkInProgress));
        };
        await update.CheckOnStartAsync();

        await update.UpdateCommand.ExecuteAsync(null);

        Assert.Equal([(true, true, true), (false, false, false)], seen);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_run_or_a_media_job_makes_the_update_wait(bool run, bool media)
    {
        var update = Create();
        var main = TestViewModels.CreateMain(_files, update);
        main.RunProfile.IsBusy = run;
        main.Media.IsBusy = media;
        await update.CheckOnStartAsync();

        await update.UpdateCommand.ExecuteAsync(null);

        Assert.Equal("old", _files.ReadAllText(Exe));
    }

    [Fact]
    public async Task Closing_during_an_update_goes_through_the_update()
    {
        _otherDownloader = new StuckDownloader();
        var update = Create();
        var main = TestViewModels.CreateMain(_files, update);
        await update.CheckOnStartAsync();
        _dialogs.ConfirmResult = true;
        _ = update.UpdateCommand.ExecuteAsync(null);

        Assert.True(await main.ConfirmCloseAsync());

        Assert.False(update.IsUpdating);
    }

    [Fact]
    public void Home_has_no_banner_without_an_updater() =>
        Assert.False(new HomeViewModel(_state).ShowUpdateBanner);
}
