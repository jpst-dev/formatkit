using FormatKit.App.Localization;
using FormatKit.App.Tests.Fakes;
using FormatKit.App.ViewModels;
using FormatKit.App.Worker;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Hosting;
using FormatKit.Media.Building;
using FormatKit.Media.Setup;
using FormatKit.Media.Usb;
using Serilog;

namespace FormatKit.App.Tests;

public sealed class MediaViewModelTests : IDisposable
{
    private const string Iso = @"C:\iso\win11.iso";
    private const string ProfileJson = """{"schemaVersion":1,"name":"Mine","modules":{"fake":{"enabled":true}}}""";
    private const long Gb = 1_000_000_000;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly MachineLocale Machine = new("pt-BR", "0416:00010416", "E. South America Standard Time");

    private static readonly DiskRow Eligible = new(3, "usb-3", "SanDisk", 16 * Gb, "USB", true,
        [new DiskVolume('G', "STICK"), new DiskVolume(null, "EFI")], UsbRefusal.None, []);

    private static readonly DiskRow HoldsProfile = new(4, "usb-4", "Kingston", 32 * Gb, "USB", true,
        [new DiskVolume('H', "DATA")], UsbRefusal.HoldsSourceFiles, [@"H:\profile.json"]);

    private static readonly DiskRow HardDisk = new(5, "usb-5", "WD Passport", 1000 * Gb, "USB", false, [], UsbRefusal.None, []);

    private readonly string _dir = Directory.CreateTempSubdirectory("fk-media-vm-").FullName;
    private readonly InMemoryFileSystem _files = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeWorkerLauncher _launcher = new();
    private readonly FakeAppElevation _elevation = new();
    private readonly FakeModule _module = new("fake");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private MediaViewModel Create(IWorkerLauncher? launcher = null)
    {
        var env = new HostEnvironment(
            TestServices.Create(files: _files), _elevation, new FakeScheduledTasks(), new FakeRebooter(), new FakeReportWriter(),
            TestViewModels.Store(_files), new LoggerConfiguration().CreateLogger(), TimeSpan.Zero, TimeSpan.Zero);
        return new MediaViewModel(new ProfileService([_module]), launcher ?? _launcher, _elevation, _dialogs, env, () => Machine);
    }

    private MediaViewModel WithIso(IWorkerLauncher? launcher = null)
    {
        var vm = Create(launcher);
        _dialogs.OpenFileResult = Iso;
        vm.ChooseIsoCommand.Execute(null);
        vm.IsUsbTarget = true;
        return vm;
    }

    private string WriteProfile(string json = ProfileJson)
    {
        var path = Path.Combine(_dir, "profile.json");
        File.WriteAllText(path, json);
        _files.Seed(path, json);
        return path;
    }

    private void ChooseProfile(MediaViewModel vm, string path)
    {
        _dialogs.OpenFileResult = path;
        vm.ChooseProfileCommand.Execute(null);
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(5, Ct);
        }
    }

    private static async Task<Task> AtDisks(MediaViewModel vm)
    {
        var running = vm.ContinueCommand.ExecuteAsync(null);
        await Until(() => vm.Stage == MediaStage.ChooseDisk);
        return running;
    }

    private static string Row(MediaViewModel vm, string labelKey) =>
        vm.SetupRows.Single(r => r.Label == Strings.Get(labelKey)).Value;

    [Fact]
    public void Continue_NeedsTheIso() => Assert.False(Create().ContinueCommand.CanExecute(null));

    [Fact]
    public void Continue_Folder_NeedsTheFolder()
    {
        var vm = WithIso();
        vm.IsFolderTarget = true;
        Assert.False(vm.ContinueCommand.CanExecute(null));

        _dialogs.FolderResult = @"C:\out";
        vm.ChooseFolderCommand.Execute(null);

        Assert.Equal(@"C:\out", vm.OutputFolder);
        Assert.True(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public async Task Continue_Usb_StartsElevatedWithTheUsbJob()
    {
        var vm = WithIso();
        vm.AllowHardDisk = true;
        _launcher.Script.Add(new MediaFinished(MediaFinishedStatus.Cancelled, "x"));

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(["media", "--iso", Iso, "--usb", "--allow-hard-disk"], _launcher.Jobs[0]);
        Assert.True(_launcher.Elevations[0]);
    }

    [Fact]
    public async Task Continue_AlreadyElevated_NoPrompt()
    {
        _elevation.IsElevated = true;
        var vm = WithIso();
        _launcher.Script.Add(new MediaFinished(MediaFinishedStatus.Cancelled, "x"));

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.False(_launcher.Elevations[0]);
    }

    [Fact]
    public async Task Continue_Folder_WithProfile_SendsItsHash()
    {
        var path = WriteProfile();
        var vm = WithIso();
        vm.IsFolderTarget = true;
        _dialogs.FolderResult = @"C:\out";
        vm.ChooseFolderCommand.Execute(null);
        ChooseProfile(vm, path);
        _launcher.Script.Add(new MediaFinished(MediaFinishedStatus.Done, "ok"));

        await vm.ContinueCommand.ExecuteAsync(null);

        var hash = new ProfileService([_module]).Load(path).Profile!.Hash;
        Assert.Equal(["media", "--iso", Iso, "--profile", path, "--profile-hash", hash, "--output", @"C:\out"], _launcher.Jobs[0]);
    }

    [Fact]
    public async Task UacDeclined_StaysOnTheInputsWithAMessage()
    {
        _launcher.Declines = true;
        var vm = WithIso();

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(MediaStage.Inputs, vm.Stage);
        Assert.Equal(Strings.Get("RunProfile_UacDeclined"), vm.Message);
    }

    [Fact]
    public async Task Folder_NotEmpty_Refused()
    {
        // The worker holds the rule; the page shows its refusal on the inputs, with the choices kept.
        var vm = WithIso();
        vm.IsFolderTarget = true;
        _dialogs.FolderResult = @"C:\out";
        vm.ChooseFolderCommand.Execute(null);
        _launcher.Script.Add(new Failed("C:\\out already holds files."));

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(MediaStage.Inputs, vm.Stage);
        Assert.Equal("C:\\out already holds files.", vm.Message);
        Assert.Equal(@"C:\out", vm.OutputFolder);
        Assert.False(vm.IsRunInProgress);
    }

    [Fact]
    public async Task Usb_DisksListed_EligibleAndIneligibleWithReason()
    {
        var vm = WithIso();
        _launcher.Will(new Disks([Eligible, HoldsProfile], 2), new Expect<Cancel>());

        var running = await AtDisks(vm);

        Assert.True(vm.Disks[0].IsEligible);
        Assert.Contains("SanDisk", vm.Disks[0].Title);
        Assert.Contains("3", vm.Disks[0].Title);
        Assert.Contains("USB", vm.Disks[0].Title);
        Assert.Contains("G:", vm.Disks[0].Volumes);
        Assert.Contains("STICK", vm.Disks[0].Volumes);
        Assert.Null(vm.Disks[0].Reason);

        Assert.False(vm.Disks[1].IsEligible);
        Assert.Equal(
            Strings.Format("Media_Refusal_HoldsSourceFiles", @"H:\profile.json"),
            vm.Disks[1].Reason);
        Assert.Equal(Strings.Format("Media_HardDisksHidden", 2), vm.HiddenHardDisksText);

        await vm.StopCommand.ExecuteAsync(null);
        await running;
    }

    [Fact]
    public async Task Usb_NoUsbDisk_SaysSo()
    {
        var vm = WithIso();
        _launcher.Will(new Disks([], 0), new Expect<Cancel>());

        var running = await AtDisks(vm);

        Assert.True(vm.HasNoDisks);
        await vm.StopCommand.ExecuteAsync(null);
        await running;
    }

    [Fact]
    public async Task Usb_Erase_NeedsAnEligibleDiskAndATypedNumber()
    {
        var vm = WithIso();
        _launcher.Will(new Disks([Eligible, HoldsProfile], 0), new Expect<Cancel>());
        var running = await AtDisks(vm);

        Assert.False(vm.EraseCommand.CanExecute(null));
        vm.TypedNumber = "3";
        Assert.False(vm.EraseCommand.CanExecute(null));

        vm.SelectedDisk = vm.Disks[1];
        vm.TypedNumber = "4";
        Assert.False(vm.EraseCommand.CanExecute(null));

        vm.SelectedDisk = vm.Disks[0];
        Assert.Equal(string.Empty, vm.TypedNumber);
        Assert.False(vm.EraseCommand.CanExecute(null));
        vm.TypedNumber = "3";
        Assert.True(vm.EraseCommand.CanExecute(null));

        await vm.StopCommand.ExecuteAsync(null);
        await running;
    }

    [Fact]
    public async Task Usb_SelectedDisk_WarnsThatEverythingIsErased()
    {
        var vm = WithIso();
        _launcher.Will(new Disks([Eligible, HardDisk], 0), new Expect<Cancel>());
        var running = await AtDisks(vm);

        vm.SelectedDisk = vm.Disks[0];
        Assert.Equal(Strings.Format("Media_EraseWarning", 3), vm.EraseWarning);
        Assert.False(vm.IsHardDiskSelected);

        vm.SelectedDisk = vm.Disks[1];
        Assert.Equal(Strings.Format("Media_EraseWarning", 5), vm.EraseWarning);
        Assert.True(vm.IsHardDiskSelected);

        await vm.StopCommand.ExecuteAsync(null);
        await running;
    }

    [Fact]
    public async Task Usb_Erase_SendsWhatWasTypedAndTheSelectedDisk()
    {
        var vm = WithIso();
        _launcher.Will(new Disks([Eligible], 0), new Expect<ConfirmDisk>(),
            new MediaFinished(MediaFinishedStatus.Cancelled, Strings.Get("Media_Cancelled")));
        var running = await AtDisks(vm);

        vm.SelectedDisk = vm.Disks[0];
        vm.TypedNumber = "7";
        await vm.EraseCommand.ExecuteAsync(null);
        await running;

        Assert.Equal(new ConfirmDisk("usb-3", "7"), _launcher.Received.OfType<ConfirmDisk>().Single());
    }

    [Fact]
    public async Task Usb_WrongNumberTyped_ShowsNothingWasErased()
    {
        var vm = WithIso();
        _launcher.Will(new Disks([Eligible], 0), new Expect<ConfirmDisk>(),
            new MediaFinished(MediaFinishedStatus.Cancelled, Strings.Get("Media_Cancelled")));
        var running = await AtDisks(vm);

        vm.SelectedDisk = vm.Disks[0];
        vm.TypedNumber = "9";
        await vm.EraseCommand.ExecuteAsync(null);
        await running;

        Assert.Equal(MediaStage.Result, vm.Stage);
        Assert.Equal(Strings.Get("Media_Cancelled"), vm.FinalStatus);
        Assert.False(vm.ShowChecklistReminder);
    }

    [Fact]
    public async Task Usb_Success_ShowsReadyAndChecklistReminder()
    {
        var vm = WithIso();
        _launcher.Will(new Disks([Eligible], 0), new Expect<ConfirmDisk>(),
            new MediaProgressUpdate(MediaPhase.Copying, 500, 1000),
            new MediaFinished(MediaFinishedStatus.Done, Strings.Get("Media_UsbDone")));
        var running = await AtDisks(vm);

        vm.SelectedDisk = vm.Disks[0];
        vm.TypedNumber = "3";
        await vm.EraseCommand.ExecuteAsync(null);
        await running;

        Assert.Equal(MediaStage.Result, vm.Stage);
        Assert.Equal(Strings.Get("Media_UsbDone"), vm.FinalStatus);
        Assert.True(vm.ShowChecklistReminder);
        Assert.False(vm.IsRunInProgress);
    }

    [Fact]
    public async Task Progress_ShowsThePhaseAndPercent()
    {
        var gate = new TaskCompletionSource();
        var vm = WithIso();
        vm.IsFolderTarget = true;
        _dialogs.FolderResult = @"C:\out";
        vm.ChooseFolderCommand.Execute(null);
        _launcher.Will(new MediaProgressUpdate(MediaPhase.Copying, 250, 1000), new Do(() => gate.Task));

        var running = vm.ContinueCommand.ExecuteAsync(null);
        await Until(() => vm.Percent == 25);

        Assert.Equal(MediaStage.Writing, vm.Stage);
        Assert.Equal(Strings.Get("Media_Phase_Copying"), vm.StatusText);

        gate.SetResult();
        await running;
    }

    [Fact]
    public async Task Usb_FailureAfterErase_SaysIncomplete()
    {
        var text = Strings.Format("Media_UsbIncomplete", "disk full");
        var vm = WithIso();
        _launcher.Will(new Disks([Eligible], 0), new Expect<ConfirmDisk>(),
            new MediaFinished(MediaFinishedStatus.Incomplete, text));
        var running = await AtDisks(vm);

        vm.SelectedDisk = vm.Disks[0];
        vm.TypedNumber = "3";
        await vm.EraseCommand.ExecuteAsync(null);
        await running;

        Assert.Equal(text, vm.FinalStatus);
        Assert.False(vm.ShowChecklistReminder);
    }

    [Fact]
    public async Task Usb_DiskChangedBeforeErase_ShowsIt()
    {
        var text = Strings.Format("Media_DiskChanged", 3);
        var vm = WithIso();
        _launcher.Will(new Disks([Eligible], 0), new Expect<ConfirmDisk>(),
            new MediaFinished(MediaFinishedStatus.DiskChanged, text));
        var running = await AtDisks(vm);

        vm.SelectedDisk = vm.Disks[0];
        vm.TypedNumber = "3";
        await vm.EraseCommand.ExecuteAsync(null);
        await running;

        Assert.Equal(text, vm.FinalStatus);
    }

    [Fact]
    public async Task WorkerEndsWhileChoosing_SaysNothingWasErased()
    {
        var vm = WithIso();
        _launcher.Script.Add(new Disks([Eligible], 0));

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(MediaStage.Result, vm.Stage);
        Assert.Equal(Strings.Get("Media_EndedBeforeErase"), vm.FinalStatus);
        Assert.True(vm.EndedWithoutResult);
    }

    [Fact]
    public async Task WorkerEndsAfterTheConfirmation_SaysTheStickMayBeIncomplete()
    {
        var vm = WithIso();
        _launcher.Will(new Disks([Eligible], 0), new Expect<ConfirmDisk>());
        var running = await AtDisks(vm);

        vm.SelectedDisk = vm.Disks[0];
        vm.TypedNumber = "3";
        await vm.EraseCommand.ExecuteAsync(null);
        await running;

        Assert.Equal(Strings.Get("Media_EndedAfterErase"), vm.FinalStatus);
        Assert.True(vm.EndedWithoutResult);
    }

    [Fact]
    public async Task WorkerEndsDuringAFolderJob_SaysTheFolderMayBeIncomplete()
    {
        var vm = WithIso();
        vm.IsFolderTarget = true;
        _dialogs.FolderResult = @"C:\out";
        vm.ChooseFolderCommand.Execute(null);

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Format("Media_EndedFolder", @"C:\out"), vm.FinalStatus);
    }

    [Fact]
    public async Task ConsoleLine_IsShownOnThePage()
    {
        var vm = WithIso();
        _launcher.Will(new ConsoleLine("Could not dismount"), new MediaFinished(MediaFinishedStatus.Done, "ok"));

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(["Could not dismount"], vm.Notes);
    }

    [Fact]
    public async Task Back_ReturnsToTheInputsWithTheChoicesKept()
    {
        var vm = WithIso();
        _launcher.Script.Add(new MediaFinished(MediaFinishedStatus.Cancelled, "x"));
        await vm.ContinueCommand.ExecuteAsync(null);

        vm.BackCommand.Execute(null);

        Assert.Equal(MediaStage.Inputs, vm.Stage);
        Assert.Equal(Iso, vm.IsoPath);
        Assert.True(vm.IsUsbTarget);
        Assert.True(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public async Task Close_NoWork_ClosesWithoutAsking()
    {
        Assert.True(await Create().ConfirmCloseAsync());
        Assert.Empty(_dialogs.Confirmations);
    }

    [Fact]
    public async Task Close_WhileChoosing_AnswerNo_KeepsGoing()
    {
        _dialogs.ConfirmResult = false;
        var vm = WithIso();
        _launcher.Will(new Disks([Eligible], 0), new Expect<Cancel>());
        var running = await AtDisks(vm);

        Assert.False(await vm.ConfirmCloseAsync());
        Assert.Empty(_launcher.Received);

        await vm.StopCommand.ExecuteAsync(null);
        await running;
    }

    [Fact]
    public async Task Close_WhileChoosing_AnswerYes_SendsCancelAndWaitsForTheEnd()
    {
        var vm = WithIso();
        _launcher.Will(new Disks([Eligible], 0), new Expect<Cancel>(),
            new MediaFinished(MediaFinishedStatus.Cancelled, "x"));
        var running = await AtDisks(vm);

        Assert.True(await vm.ConfirmCloseAsync().WaitAsync(Timeout, Ct));

        Assert.Equal(MediaStage.Result, vm.Stage);
        await running;
    }

    [Fact]
    public async Task Close_DuringTheUacPrompt_CancelsTheStart()
    {
        var gate = new TaskCompletionSource();
        _launcher.StartGate = gate.Task;
        var vm = WithIso();

        var running = vm.ContinueCommand.ExecuteAsync(null);
        await Until(() => _launcher.Jobs.Count == 1);
        Assert.True(await vm.ConfirmCloseAsync().WaitAsync(Timeout, Ct));

        Assert.True(_launcher.StartToken.IsCancellationRequested);
        await running;
    }

    [Fact]
    public void OtherWorkRunning_BlocksContinue()
    {
        var vm = WithIso();
        Assert.True(vm.ContinueCommand.CanExecute(null));

        vm.OtherWorkRunning = true;

        Assert.False(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void SetupSection_ShowsDefaultsForMissingFields()
    {
        var vm = WithIso();

        ChooseProfile(vm, WriteProfile(
            """{"schemaVersion":1,"name":"Mine","modules":{"fake":{"enabled":true}},"setup":{"edition":"Windows 11 Pro","computerName":"PC-1"}}"""));

        Assert.Equal("Mine", vm.ProfileName);
        Assert.Equal("Windows 11 Pro", Row(vm, "Media_Setup_Edition"));
        Assert.Equal("PC-1", Row(vm, "Media_Setup_ComputerName"));
        Assert.Equal(Strings.Get("Media_Setup_Account_Local"), Row(vm, "Media_Setup_Account"));
        Assert.Equal(Strings.Format("Media_Setup_LanguageDefault", "pt-BR"), Row(vm, "Media_Setup_Language"));
        Assert.Equal(Strings.Format("Media_Setup_KeyboardDefault", "0416:00010416"), Row(vm, "Media_Setup_Keyboard"));
        Assert.Equal(Strings.Format("Media_Setup_TimeZoneDefault", "E. South America Standard Time"), Row(vm, "Media_Setup_TimeZone"));
    }

    [Fact]
    public void SetupSection_MissingEditionAndNameShowTheirDefaults()
    {
        var vm = WithIso();

        ChooseProfile(vm, WriteProfile());

        Assert.Equal(Strings.Get("Media_Setup_EditionDefault"), Row(vm, "Media_Setup_Edition"));
        Assert.Equal(Strings.Get("Media_Setup_ComputerNameDefault"), Row(vm, "Media_Setup_ComputerName"));
    }

    [Fact]
    public void SetupSection_FieldsOfTheProfileWin()
    {
        var vm = WithIso();

        ChooseProfile(vm, WriteProfile(
            """{"schemaVersion":1,"name":"Mine","modules":{"fake":{"enabled":true}},"setup":{"account":"microsoft","language":"en-US","keyboard":"0409:00000409","timeZone":"UTC"}}"""));

        Assert.Equal(Strings.Get("Media_Setup_Account_Microsoft"), Row(vm, "Media_Setup_Account"));
        Assert.Equal("en-US", Row(vm, "Media_Setup_Language"));
        Assert.Equal("0409:00000409", Row(vm, "Media_Setup_Keyboard"));
        Assert.Equal("UTC", Row(vm, "Media_Setup_TimeZone"));
    }

    [Fact]
    public void SetupSection_WithoutAProfile_ShowsTheDefaults()
    {
        var vm = Create();

        Assert.Equal(Strings.Format("Media_Setup_LanguageDefault", "pt-BR"), Row(vm, "Media_Setup_Language"));
    }

    [Fact]
    public void InvalidProfile_ListsTheErrorsAndBlocksContinue()
    {
        var vm = WithIso();

        ChooseProfile(vm, WriteProfile("""{"schemaVersion":1}"""));

        Assert.True(vm.HasProfileErrors);
        Assert.False(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void InvalidSetupSection_ListsTheErrorsAndBlocksContinue()
    {
        var vm = WithIso();

        ChooseProfile(vm, WriteProfile(
            """{"schemaVersion":1,"name":"Mine","modules":{"fake":{"enabled":true}},"setup":{"account":"nobody"}}"""));

        Assert.Contains(vm.ProfileErrors, e => e.Path == "setup.account");
        Assert.False(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void ClearProfile_BackToAPlainStick()
    {
        var vm = WithIso();
        ChooseProfile(vm, WriteProfile("""{"schemaVersion":1}"""));

        vm.ClearProfileCommand.Execute(null);

        Assert.Null(vm.ProfilePath);
        Assert.False(vm.HasProfileErrors);
        Assert.True(vm.ContinueCommand.CanExecute(null));
    }
}

/// <summary>The page over the real worker job and fake disks: the whole path from the text box to the erase.</summary>
public sealed class MediaPageEndToEndTests
{
    private const string Iso = @"C:\iso\win11.iso";
    private const string Id = "usb-3";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryFileSystem _files = new();
    private readonly FakeDisks _disks = new(new DiskInfo(3, Id, "SanDisk", 16_000_000_000, "USB", true, false, false, [new DiskVolume('G', "STICK")]));
    private readonly FakeDialogService _dialogs = new();

    private sealed class InProcessWorker(MediaPageEndToEndTests test) : IWorkerLauncher
    {
        public Task Running { get; private set; } = Task.CompletedTask;

        public Task<WorkerConnection?> StartAsync(IReadOnlyList<string> job, bool elevate, CancellationToken ct)
        {
            var (workerEnd, windowEnd) = DuplexStream.CreatePair();
            var log = new LoggerConfiguration().CreateLogger();
            var worker = new MessageChannel(workerEnd, log);
            var rebooter = new PipeRebooter(worker, new FakeProcessRunner(), log);
            var images = new FakeWindowsImages(test._files);
            images.Images[@"E:\sources\install.wim"] = [new WindowsImageInfo(1, "Windows 11 Pro", 9, "10.0.26100.1742", ["en-US"], 0)];
            var env = new HostEnvironment(
                TestServices.Create(files: test._files, diskImages: new FakeDiskImages(), windowsImages: images, disks: test._disks),
                new Elevation(), new FakeScheduledTasks(), rebooter, new FakeReportWriter(), TestViewModels.Store(test._files),
                log, TimeSpan.Zero, TimeSpan.Zero);
            var exit = Task.Run(async () =>
            {
                var code = await new WorkerHost(env, [], rebooter, @"C:\app\FormatKitUI.exe").RunAsync(worker, job, CancellationToken.None);
                await workerEnd.DisposeAsync();
                return code;
            });
            Running = exit;
            return Task.FromResult<WorkerConnection?>(new WorkerConnection(new MessageChannel(windowEnd, log), exit));
        }
    }

    private async Task<(MediaViewModel Vm, Task Running)> AtDisks()
    {
        _files.Seed(@"E:\efi\boot\bootx64.efi", "efi");
        _files.SeedEntry(@"E:\sources\install.wim", 1000);
        var env = new HostEnvironment(
            TestServices.Create(files: _files), new FakeAppElevation(), new FakeScheduledTasks(), new FakeRebooter(), new FakeReportWriter(),
            TestViewModels.Store(_files), new LoggerConfiguration().CreateLogger(), TimeSpan.Zero, TimeSpan.Zero);
        var vm = new MediaViewModel(new ProfileService([]), new InProcessWorker(this), new FakeAppElevation(), _dialogs, env,
            () => new MachineLocale("en-US", null, "UTC"));
        _dialogs.OpenFileResult = Iso;
        vm.ChooseIsoCommand.Execute(null);
        vm.IsUsbTarget = true;

        var running = vm.ContinueCommand.ExecuteAsync(null);
        var deadline = DateTime.UtcNow + Timeout;
        while (vm.Stage != MediaStage.ChooseDisk)
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(5, Ct);
        }

        return (vm, running);
    }

    [Fact]
    public async Task WrongNumber_NeverErases()
    {
        var (vm, running) = await AtDisks();

        vm.SelectedDisk = vm.Disks.Single();
        vm.TypedNumber = "33";
        await vm.EraseCommand.ExecuteAsync(null);
        await running.WaitAsync(Timeout, Ct);

        Assert.Empty(_disks.Prepared);
        Assert.Equal(Strings.Get("Media_Cancelled"), vm.FinalStatus);
    }

    [Fact]
    public async Task SwappedStick_NeverErases()
    {
        var (vm, running) = await AtDisks();
        _disks.BeforeSecondList = () => _disks.Disks[0] = _disks.Disks[0] with { UniqueId = "another" };

        vm.SelectedDisk = vm.Disks.Single();
        vm.TypedNumber = "3";
        await vm.EraseCommand.ExecuteAsync(null);
        await running.WaitAsync(Timeout, Ct);

        Assert.Empty(_disks.Prepared);
        Assert.Equal(Strings.Format("Media_DiskChanged", 3), vm.FinalStatus);
    }

    [Fact]
    public async Task RightNumber_ErasesAndWrites()
    {
        var (vm, running) = await AtDisks();

        vm.SelectedDisk = vm.Disks.Single();
        vm.TypedNumber = "3";
        await vm.EraseCommand.ExecuteAsync(null);
        await running.WaitAsync(Timeout, Ct);

        Assert.Equal([(3, Id)], _disks.Prepared);
        Assert.True(_files.FileExists(@"F:\autounattend.xml"));
        Assert.Equal(Strings.Get("Media_UsbDone"), vm.FinalStatus);
        Assert.True(vm.ShowChecklistReminder);
    }

    [Fact]
    public async Task Cancel_NeverErases()
    {
        var (vm, running) = await AtDisks();

        await vm.StopCommand.ExecuteAsync(null);
        await running.WaitAsync(Timeout, Ct);

        Assert.Empty(_disks.Prepared);
        Assert.Equal(Strings.Get("Media_Cancelled"), vm.FinalStatus);
    }
}
