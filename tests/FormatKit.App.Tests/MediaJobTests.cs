using FormatKit.App.Localization;
using FormatKit.App.Tests.Fakes;
using FormatKit.App.Worker;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Hosting;
using FormatKit.Media.Usb;
using Serilog;

namespace FormatKit.App.Tests;

/// <summary>The worker's media job over an in-memory pipe, with fake disks: no test here touches a real disk.</summary>
public sealed class MediaJobTests : IAsyncDisposable
{
    private const string Iso = @"C:\iso\win11.iso";
    private const string Ui = @"C:\app\FormatKitUI.exe";
    private const string Cli = @"C:\app\formatkit.exe";
    private const string Output = @"C:\out";
    private const string Id = "usb-3";
    private const long Gb = 1_000_000_000;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static readonly DiskInfo Stick = new(3, Id, "SanDisk", 16 * Gb, "USB", true, false, false, [new DiskVolume('G', "STICK")]);
    private static readonly DiskInfo SystemDisk = new(0, "nvme-0", "Samsung", 500 * Gb, "NVMe", false, true, true, [new DiskVolume('C', "Windows")]);

    private readonly string _dir = Directory.CreateTempSubdirectory("fk-media-job-").FullName;
    private readonly InMemoryFileSystem _files = new();
    private readonly FakeDiskImages _mounts = new();
    private readonly FakeVirtualDisks _virtualDisks = new();
    private readonly FakeDisks _disks = new(SystemDisk, Stick);
    private readonly FakeWindowsImages _images;
    private readonly FakeModule _module = new("fake");
    private readonly DuplexStream _workerEnd;
    private readonly DuplexStream _windowEnd;
    private readonly MessageChannel _worker;
    private readonly MessageChannel _window;
    private readonly WorkerHost _host;

    public MediaJobTests()
    {
        _files.Seed(Ui, "UI");
        _files.Seed(Cli, "CLI");
        _files.Seed(@"E:\efi\boot\bootx64.efi", "efi");
        _files.SeedEntry(@"E:\sources\install.wim", 1000);
        _images = new FakeWindowsImages(_files);
        _images.Images[@"E:\sources\install.wim"] = [new WindowsImageInfo(1, "Windows 11 Pro", 9, "10.0.26100.1742", ["en-US"], 0)];

        (_workerEnd, _windowEnd) = DuplexStream.CreatePair();
        var log = new LoggerConfiguration().CreateLogger();
        _worker = new MessageChannel(_workerEnd, log);
        _window = new MessageChannel(_windowEnd, log);
        var rebooter = new PipeRebooter(_worker, new FakeProcessRunner(), log);
        var env = new HostEnvironment(
            // As WorkerProcess wires it.
            TestServices.Create(files: _files, diskImages: _mounts, windowsImages: _images, virtualDisks: _virtualDisks, disks: _disks)
                with { Console = new PipeInteractiveConsole(new FakeInteractiveConsole(), _worker) },
            new Elevation(), new FakeScheduledTasks(), rebooter, new FakeReportWriter(),
            new RunStateStore(_files, new AppPaths(@"C:\FormatKitTest")), log, TimeSpan.Zero, TimeSpan.Zero);
        _host = new WorkerHost(env, [_module], rebooter, Ui);
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _window.DisposeAsync();
        Directory.Delete(_dir, recursive: true);
    }

    private static string[] UsbJob(params string[] extra) => ["media", "--iso", Iso, "--usb", .. extra];

    private string WriteProfile()
    {
        var path = Path.Combine(_dir, "profile.json");
        const string json = """{"schemaVersion":1,"name":"Mine","modules":{"fake":{"enabled":true}}}""";
        File.WriteAllText(path, json);
        _files.Seed(path, json);
        return path;
    }

    private string HashOf(string path) => new ProfileService([_module]).Load(path).Profile!.Hash;

    /// <summary>Runs a job to its end and returns what the window received.</summary>
    private async Task<(int Exit, List<WorkerMessage> Messages)> Run(params string[] job)
    {
        var exit = await _host.RunAsync(_worker, job, Ct);
        await _worker.DisposeAsync();
        var messages = new List<WorkerMessage>();
        await foreach (var m in _window.ReadAllAsync(Ct)) messages.Add(m);
        return (exit, messages);
    }

    /// <summary>Starts a USB job, waits for the disk list, runs <paramref name="answer"/> and returns what came after it.</summary>
    private async Task<(int Exit, Disks Disks, List<WorkerMessage> After)> RunUsb(Func<Task> answer, params string[] extra)
    {
        var running = Task.Run(() => _host.RunAsync(_worker, UsbJob(extra), Ct), Ct);
        await using var reader = _window.ReadAllAsync(Ct).GetAsyncEnumerator(Ct);
        Disks? disks = null;
        while (disks is null)
        {
            Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(Wait, Ct), "the worker ended before sending the disks");
            disks = reader.Current as Disks;
        }

        await answer();
        var exit = await running.WaitAsync(Wait, Ct);
        await _worker.DisposeAsync();
        var rest = new List<WorkerMessage>();
        while (await reader.MoveNextAsync().AsTask().WaitAsync(Wait, Ct))
            rest.Add(reader.Current);
        return (exit, disks, rest);
    }

    private Task Type(string typed, string id = Id) => _window.SendAsync(new ConfirmDisk(id, typed), Ct);

    [Fact]
    public async Task Folder_NotEmpty_Refused()
    {
        _files.Seed(@"C:\out\old.txt", "x");

        var (exit, messages) = await Run("media", "--iso", Iso, "--output", Output);

        Assert.Equal(WorkerHost.ExitFailed, exit);
        Assert.Equal(Strings.Format("Media_OutputNotEmpty", Output), Assert.IsType<Failed>(Assert.Single(messages)).Message);
        Assert.Empty(_mounts.Mounted);
    }

    [Fact]
    public async Task Folder_Empty_WritesTheMediaAndReportsDone()
    {
        var (exit, messages) = await Run("media", "--iso", Iso, "--output", Output);

        Assert.Equal(WorkerHost.ExitFinished, exit);
        Assert.Contains(messages, m => m is MediaProgressUpdate);
        var done = Assert.IsType<MediaFinished>(messages[^1]);
        Assert.Equal(MediaFinishedStatus.Done, done.Status);
        Assert.Equal(Strings.Format("Media_FolderDone", Output), done.Message);
        Assert.True(_files.FileExists(@"C:\out\autounattend.xml"));
        Assert.Equal([Iso], _mounts.Dismounted);
        Assert.Empty(_disks.Prepared);
    }

    [Fact]
    public async Task Folder_NotEnoughSpace_RefusedAndDismounted()
    {
        _virtualDisks.FreeBytesValue = 10;

        var (exit, messages) = await Run("media", "--iso", Iso, "--output", Output);

        Assert.Equal(WorkerHost.ExitFailed, exit);
        Assert.IsType<Failed>(Assert.Single(messages));
        Assert.Equal([Iso], _mounts.Dismounted);
        Assert.False(_files.FileExists(@"C:\out\autounattend.xml"));
    }

    [Fact]
    public async Task Folder_WriteFailure_SaysIncomplete()
    {
        _files.SeedEntry(@"E:\sources\install.wim", 5L << 30);
        _images.ThrowOnSplit = new InvalidOperationException("dism failed");

        var (exit, messages) = await Run("media", "--iso", Iso, "--output", Output);

        Assert.Equal(WorkerHost.ExitFinished, exit);
        var finished = Assert.IsType<MediaFinished>(messages[^1]);
        Assert.Equal(MediaFinishedStatus.Incomplete, finished.Status);
        Assert.Contains(Output, finished.Message);
    }

    [Fact]
    public async Task Profile_WithoutTheCliExe_RefusedBeforeMounting()
    {
        var path = WriteProfile();
        Assert.True(_files.FileExists(Cli));
        _files.Delete(Cli);

        var (_, messages) = await Run("media", "--iso", Iso, "--profile", path, "--profile-hash", HashOf(path), "--output", Output);

        Assert.Equal(Strings.Format("Media_CliExeMissing", Cli), Assert.IsType<Failed>(Assert.Single(messages)).Message);
        Assert.Empty(_mounts.Mounted);
    }

    [Fact]
    public async Task Profile_BundlesTheCliExe_NotTheRunningExe()
    {
        var path = WriteProfile();

        await Run("media", "--iso", Iso, "--profile", path, "--profile-hash", HashOf(path), "--output", Output);

        Assert.Equal("CLI", _files.ReadAllText(@"C:\out\sources\$OEM$\$1\FormatKit\formatkit.exe"));
    }

    [Fact]
    public async Task NoProfile_NeedsNoCliExe()
    {
        _files.Delete(Cli);

        var (_, messages) = await Run("media", "--iso", Iso, "--output", Output);

        Assert.Equal(MediaFinishedStatus.Done, Assert.IsType<MediaFinished>(messages[^1]).Status);
    }

    [Fact]
    public async Task Profile_ChangedSinceTheReview_RefusedBeforeMounting()
    {
        var path = WriteProfile();

        var (_, messages) = await Run("media", "--iso", Iso, "--profile", path, "--profile-hash", "old", "--output", Output);

        Assert.Equal(FailureReason.ProfileChanged, Assert.IsType<Failed>(Assert.Single(messages)).Reason);
        Assert.Empty(_mounts.Mounted);
    }

    [Fact]
    public async Task Profile_Invalid_ListsTheErrors()
    {
        var path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, """{"schemaVersion":1}""");

        var (_, messages) = await Run("media", "--iso", Iso, "--profile", path, "--profile-hash", "x", "--output", Output);

        Assert.StartsWith(Strings.Format("Worker_ProfileErrors", 1)[..8], Assert.IsType<Failed>(Assert.Single(messages)).Message);
        Assert.Empty(_mounts.Mounted);
    }

    [Fact]
    public async Task InvalidIso_SendsTheMediaMessage()
    {
        _images.Images[@"E:\sources\install.wim"] = [];

        var (exit, messages) = await Run(UsbJob());

        Assert.Equal(WorkerHost.ExitFailed, exit);
        Assert.IsType<Failed>(Assert.Single(messages));
        Assert.Equal([Iso], _mounts.Dismounted);
        Assert.Empty(_disks.Prepared);
    }

    [Theory]
    [InlineData("media")]
    [InlineData("media", "--iso", Iso)]
    [InlineData("media", "--iso", Iso, "--usb", "--output", Output)]
    public async Task InvalidJob_Refused(params string[] job)
    {
        var (exit, messages) = await Run(job);

        Assert.Equal(WorkerHost.ExitFailed, exit);
        Assert.IsType<Failed>(Assert.Single(messages));
        Assert.Empty(_mounts.Mounted);
    }

    [Fact]
    public async Task Usb_WindowsReturnsNoDisks_SaysSoAndErasesNothing()
    {
        _disks.Disks.Clear();

        var (exit, messages) = await Run(UsbJob());

        Assert.Equal(WorkerHost.ExitFailed, exit);
        Assert.Equal(Strings.Get("Media_NoDisksAtAll"), Assert.IsType<Failed>(Assert.Single(messages)).Message);
        Assert.Equal([Iso], _mounts.Dismounted);
    }

    [Fact]
    public async Task Usb_DisksListed_OnlyUsb_WithRefusalAndHiddenHardDisks()
    {
        var tooSmall = Stick with { Number = 4, UniqueId = "small", SizeBytes = 1 };
        var hardDisk = Stick with { Number = 5, UniqueId = "hd", IsRemovable = false };
        _disks.Disks.AddRange([tooSmall, hardDisk]);

        var (_, disks, _) = await RunUsb(() => _window.SendAsync(new Cancel(), Ct));

        Assert.Equal([3, 4], disks.Items.Select(d => d.Number));
        Assert.Equal(UsbRefusal.None, disks.Items[0].Refusal);
        Assert.Equal(UsbRefusal.TooSmall, disks.Items[1].Refusal);
        Assert.Equal(1, disks.HiddenHardDisks);
    }

    [Fact]
    public async Task Usb_AllowHardDisk_ListsItAsEligible()
    {
        var hardDisk = Stick with { Number = 5, UniqueId = "hd", IsRemovable = false };
        _disks.Disks.Add(hardDisk);

        var (_, disks, _) = await RunUsb(() => _window.SendAsync(new Cancel(), Ct), "--allow-hard-disk");

        Assert.Equal(UsbRefusal.None, disks.Items.Single(d => d.Number == 5).Refusal);
        Assert.Equal(0, disks.HiddenHardDisks);
    }

    [Fact]
    public async Task Usb_TheRunningExeOnTheStick_IsRefused()
    {
        _disks.Disks.Add(Stick with { Number = 6, UniqueId = "app", Volumes = [new DiskVolume('C', "APP")] });

        var (_, disks, _) = await RunUsb(() => _window.SendAsync(new Cancel(), Ct));

        var row = disks.Items.Single(d => d.Number == 6);
        Assert.Equal(UsbRefusal.HoldsSourceFiles, row.Refusal);
        Assert.Contains(Ui, row.SourcesOnDisk);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("")]
    [InlineData("yes")]
    public async Task Usb_WrongNumberTyped_CancelsWithoutErase(string typed)
    {
        var (exit, _, rest) = await RunUsb(() => Type(typed));

        Assert.Equal(WorkerHost.ExitFinished, exit);
        var finished = Assert.IsType<MediaFinished>(Assert.Single(rest));
        Assert.Equal(MediaFinishedStatus.Cancelled, finished.Status);
        Assert.Equal(Strings.Get("Media_Cancelled"), finished.Message);
        Assert.Empty(_disks.Prepared);
        Assert.Equal([Iso], _mounts.Dismounted);
    }

    [Fact]
    public async Task Usb_DiskNotInTheList_CancelsWithoutErase()
    {
        var (_, _, rest) = await RunUsb(() => Type("3", "someone-else"));

        Assert.Equal(MediaFinishedStatus.Cancelled, Assert.IsType<MediaFinished>(Assert.Single(rest)).Status);
        Assert.Empty(_disks.Prepared);
    }

    [Fact]
    public async Task Usb_RefusedDiskConfirmed_CancelsWithoutErase()
    {
        // Internal disks are not offered; a window sending one anyway must not erase it.
        var (_, _, rest) = await RunUsb(() => Type("0", "nvme-0"));

        Assert.Equal(MediaFinishedStatus.Cancelled, Assert.IsType<MediaFinished>(Assert.Single(rest)).Status);
        Assert.Empty(_disks.Prepared);
    }

    [Fact]
    public async Task Usb_StopWhileChoosing_CancelsWithoutErase()
    {
        var (_, _, rest) = await RunUsb(() => _window.SendAsync(new Cancel(), Ct));

        Assert.Equal(MediaFinishedStatus.Cancelled, Assert.IsType<MediaFinished>(Assert.Single(rest)).Status);
        Assert.Empty(_disks.Prepared);
        Assert.Equal([Iso], _mounts.Dismounted);
    }

    [Fact]
    public async Task Usb_WindowGoneWhileChoosing_ErasesNothingAndDismounts()
    {
        var running = Task.Run(() => _host.RunAsync(_worker, UsbJob(), Ct), Ct);
        await foreach (var message in _window.ReadAllAsync(Ct))
        {
            if (message is Disks)
                break;
        }

        await _windowEnd.DisposeAsync();
        await running.WaitAsync(Wait, Ct);

        Assert.Empty(_disks.Prepared);
        Assert.Equal([Iso], _mounts.Dismounted);
    }

    [Fact]
    public async Task Usb_DiskChangedBeforeErase_StopsWithoutErase()
    {
        _disks.BeforeSecondList = () =>
        {
            _disks.Disks.Remove(Stick);
            _disks.Disks.Add(Stick with { UniqueId = "another-stick" });
        };

        var (_, _, rest) = await RunUsb(() => Type("3"));

        var finished = Assert.IsType<MediaFinished>(Assert.Single(rest));
        Assert.Equal(MediaFinishedStatus.DiskChanged, finished.Status);
        Assert.Equal(Strings.Format("Media_DiskChanged", 3), finished.Message);
        Assert.Empty(_disks.Prepared);
    }

    [Fact]
    public async Task Usb_DiskUnpluggedBeforeErase_StopsWithoutErase()
    {
        _disks.BeforeSecondList = () => _disks.Disks.Remove(Stick);

        var (_, _, rest) = await RunUsb(() => Type("3"));

        Assert.Equal(MediaFinishedStatus.DiskChanged, Assert.IsType<MediaFinished>(Assert.Single(rest)).Status);
        Assert.Empty(_disks.Prepared);
    }

    [Fact]
    public async Task Usb_RightNumber_ErasesThatDiskAndWrites()
    {
        var (exit, _, rest) = await RunUsb(() => Type(" 3 "));

        Assert.Equal(WorkerHost.ExitFinished, exit);
        Assert.Equal([(3, Id)], _disks.Prepared);
        Assert.True(_files.FileExists(@"F:\autounattend.xml"));
        var finished = Assert.IsType<MediaFinished>(rest[^1]);
        Assert.Equal(MediaFinishedStatus.Done, finished.Status);
        Assert.Equal(Strings.Get("Media_UsbDone"), finished.Message);
        Assert.Equal([Iso], _mounts.Dismounted);
    }

    [Fact]
    public async Task Usb_Progress_IsSentPerPercentNotPerFile()
    {
        for (var i = 0; i < 500; i++)
            _files.SeedEntry($@"E:\files\{i}.txt", 1);
        _files.SeedEntry(@"E:\sources\install.wim", 500);

        var (_, _, rest) = await RunUsb(() => Type("3"));

        var progress = rest.OfType<MediaProgressUpdate>().ToList();
        Assert.NotEmpty(progress);
        Assert.True(progress.Count <= 110, $"{progress.Count} progress messages");
        Assert.Equal(FormatKit.Media.Building.MediaPhase.Done, progress[^1].Phase);
    }

    [Fact]
    public async Task Usb_EraseFails_SaysIncomplete()
    {
        _disks.ThrowOnPrepare = new InvalidOperationException("diskpart exploded");

        var (exit, _, rest) = await RunUsb(() => Type("3"));

        Assert.Equal(WorkerHost.ExitFinished, exit);
        var finished = Assert.IsType<MediaFinished>(Assert.Single(rest));
        Assert.Equal(MediaFinishedStatus.Incomplete, finished.Status);
        Assert.Equal(Strings.Format("Media_UsbIncomplete", Strings.Get("Media_EraseFailed")), finished.Message);
        Assert.DoesNotContain("diskpart exploded", finished.Message);
    }

    [Fact]
    public async Task Usb_FailureAfterErase_SaysIncomplete()
    {
        _files.SeedEntry(@"E:\sources\install.wim", 5L << 30);
        _images.ThrowOnSplit = new InvalidOperationException("dism failed");

        var (_, _, rest) = await RunUsb(() => Type("3"));

        Assert.Equal([(3, Id)], _disks.Prepared);
        var finished = Assert.IsType<MediaFinished>(rest[^1]);
        Assert.Equal(MediaFinishedStatus.Incomplete, finished.Status);
        Assert.StartsWith(Strings.Format("Media_UsbIncomplete", "")[..10], finished.Message);
    }

    [Fact]
    public async Task Usb_StopDuringTheRecheck_ErasesNothing()
    {
        _disks.BeforeSecondList = StopNow;

        var (_, _, rest) = await RunUsb(() => Type("3"));

        Assert.Empty(_disks.Prepared);
        var finished = Assert.IsType<MediaFinished>(rest[^1]);
        Assert.Equal(MediaFinishedStatus.Cancelled, finished.Status);
        Assert.Equal(Strings.Get("Media_Cancelled"), finished.Message);
    }

    private void StopNow()
    {
        _window.SendAsync(new Cancel(), Ct).GetAwaiter().GetResult();
        _host.StopRequested.WaitAsync(Wait, Ct).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Usb_StopDuringTheErase_DoesNotInterruptItAndSaysIncomplete()
    {
        // The erase gets a token that is not cancelled, and the write that follows stops before copying anything.
        _disks.DuringPrepare = StopNow;

        var (_, _, rest) = await RunUsb(() => Type("3"));

        Assert.Equal([(3, Id)], _disks.Prepared);
        var finished = Assert.IsType<MediaFinished>(rest[^1]);
        Assert.Equal(MediaFinishedStatus.Incomplete, finished.Status);
        Assert.Equal(Strings.Format("Media_UsbIncomplete", Strings.Get("Media_Interrupted")), finished.Message);
        Assert.False(_files.FileExists(@"F:\autounattend.xml"));
    }

    [Fact]
    public async Task DismountFailure_ReachesTheWindowBeforeTheResult()
    {
        _mounts.ThrowOnDismount = new InvalidOperationException("still in use");

        var (_, messages) = await Run("media", "--iso", Iso, "--output", Output);

        var line = messages.FindIndex(m => m is ConsoleLine);
        Assert.InRange(line, 0, messages.Count - 2);
        Assert.IsType<MediaFinished>(messages[^1]);
    }
}
