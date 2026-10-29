using System.Globalization;
using FormatKit.Cli.Commands;
using FormatKit.Cli.Tests.Fakes;
using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Drivers.Catalog;
using FormatKit.Media.Setup;

namespace FormatKit.Cli.Tests;

[Collection("Culture")]
public sealed class MediaCommandsTests : IDisposable
{
    private const string Iso = @"C:\iso\win11.iso";
    private const string Output = @"C:\out";
    private const string Exe = @"C:\exe\formatkit.exe";
    private const string ValidProfile = """{"schemaVersion":1,"name":"T","modules":{"fake":{"enabled":true},"off":{"enabled":false}}}""";

    private readonly CultureInfo _original = CultureInfo.CurrentUICulture;
    private readonly string _tempDir = Directory.CreateTempSubdirectory("formatkit-cli-media-").FullName;
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeElevation _elevation = new() { IsElevated = true };
    private readonly FakeDiskImages _disk = new();
    private readonly FakeWindowsImages _images;
    private readonly FakeVirtualDisks _virtualDisks = new();
    private readonly FakeModule _enabled = new("fake");
    private readonly FakeModule _disabled = new("off");
    private readonly StringWriter _out = new();
    private readonly CollectingSink _log = new();
    private readonly FakeDisks _disks = new();
    private readonly FakeInteractiveConsole _console = new();
    private readonly List<string?> _typed = [];
    private Queue<string?> _answers = new();
    private int _readLineCalls;
    private IDiskImages _diskImages;

    public MediaCommandsTests()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        MediaCommands.Machine = () => new MachineLocale("en-US", "0409:00000409", "UTC");
        MediaCommands.ExePath = () => Exe;
        _fs.Seed(Exe, "EXE");
        _fs.Seed(@"E:\efi\boot\bootx64.efi", "efi");
        _fs.Seed(@"E:\boot\boot.sdi", "sdi");
        _fs.Seed(@"E:\setup.exe", "setup");
        _fs.Seed(@"E:\sources\boot.wim", "bootwim");
        _fs.SeedEntry(@"E:\sources\install.wim", 1000);
        _images = new FakeWindowsImages(_fs);
        _images.Images[@"E:\sources\install.wim"] =
            [new WindowsImageInfo(1, "Windows 11 Pro", 9, "10.0.26100.1742", ["en-US"], 0)];
        _diskImages = _disk;
        MediaCommands.ReadLine = () =>
        {
            _readLineCalls++;
            var answer = _answers.Count > 0 ? _answers.Dequeue() : null;
            _typed.Add(answer);
            return answer;
        };
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _original;
        MediaCommands.Machine = null;
        MediaCommands.ExePath = () => Environment.ProcessPath!;
        MediaCommands.ReadLine = Console.ReadLine;
        Directory.Delete(_tempDir, recursive: true);
    }

    private Task<int> Run(CancellationToken ct, params string[] args) => RunVerb("build", ct, args);

    private Task<int> RunVerb(string verb, CancellationToken ct, params string[] args)
    {
        var services = TestServices.Create(files: _fs, diskImages: _disk, windowsImages: _images, virtualDisks: _virtualDisks,
            disks: _disks, console: _console) with { DiskImages = _diskImages };
        var env = new CliEnvironment(
            services, _elevation, new FakeScheduledTasks(), new FakeRebooter(), new FakeReportWriter(),
            new RunStateStore(_fs, new AppPaths("data")),
            new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_log).CreateLogger(), _out, TimeSpan.Zero, TimeSpan.Zero);
        return new CliApp(env, VendorCatalog.LoadEmbedded(), [_enabled, _disabled])
            .RunAsync(["media", verb, .. args], ct);
    }

    private Task<int> Run(params string[] args) => Run(CancellationToken.None, args);

    private Task<int> RunBuild() => Run("--iso", Iso, "--output", Output);

    private string WriteProfile(string json = ValidProfile)
    {
        var path = Path.Combine(_tempDir, "p.json");
        File.WriteAllText(path, json);
        _fs.Seed(path, json);
        return path;
    }

    [Fact]
    public async Task Not_elevated_relaunches_before_loading_the_profile_or_mounting()
    {
        _elevation.IsElevated = false;
        _elevation.RelaunchResult = 0;

        var exit = await Run("--iso", Iso, "--output", Output, "--profile", Path.Combine(_tempDir, "missing.json"));

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal(1, _elevation.RelaunchCalls);
        Assert.Empty(_disk.Mounted);
        Assert.DoesNotContain("problem", _out.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Declined_elevation_exits_5()
    {
        _elevation.IsElevated = false;
        _elevation.RelaunchResult = null;

        var exit = await RunBuild();

        Assert.Equal(ExitCodes.ElevationDeclined, exit);
        Assert.Empty(_disk.Mounted);
    }

    [Theory]
    [InlineData("--output", Output)]
    [InlineData("--iso", Iso)]
    public async Task A_missing_required_option_exits_2(string option, string value)
    {
        var exit = await Run(option, value);

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Empty(_disk.Mounted);
    }

    [Fact]
    public async Task An_output_folder_that_is_not_empty_exits_2_without_mounting()
    {
        _fs.Seed(Output + @"\old.txt", "x");

        var exit = await RunBuild();

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains(@"C:\out already holds files", _out.ToString());
        Assert.Empty(_disk.Mounted);
    }

    [Fact]
    public async Task Not_enough_free_space_exits_2_dismounts_and_writes_nothing()
    {
        _virtualDisks.FreeBytesValue = 10;

        var exit = await RunBuild();

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains(@"Not enough free space on C:\", _out.ToString());
        Assert.Equal([Iso], _disk.Dismounted);
        Assert.False(_fs.FileExists(Output + @"\autounattend.xml"));
        Assert.False(_fs.DirectoryExists(Output));
    }

    [Fact]
    public async Task An_unreadable_free_space_exits_2_and_dismounts()
    {
        _virtualDisks.FreeBytesFailure = new IOException("not ready");

        var exit = await RunBuild();

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("not ready", _out.ToString());
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task An_invalid_profile_prints_errors_and_exits_2_without_mounting()
    {
        var profile = WriteProfile("""{"schemaVersion":1,"name":"T","modules":{"nope":{}}}""");

        var exit = await Run("--iso", Iso, "--output", Output, "--profile", profile);

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("problem", _out.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_disk.Mounted);
    }

    [Fact]
    public async Task An_invalid_setup_section_prints_its_path_and_exits_2_without_mounting()
    {
        var profile = WriteProfile("""{"schemaVersion":1,"name":"T","modules":{},"setup":{"account":"x"}}""");

        var exit = await Run("--iso", Iso, "--output", Output, "--profile", profile);

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("setup.account", _out.ToString());
        Assert.Empty(_disk.Mounted);
    }

    [Fact]
    public async Task A_missing_input_prints_its_json_path_and_exits_2_without_mounting()
    {
        _enabled.Inputs.Add(new ExternalInput("modules.fake.file", @"C:\inputs\x.json", ExternalInputKind.File));
        _disabled.Inputs.Add(new ExternalInput("modules.off.file", @"C:\inputs\off.json", ExternalInputKind.File));
        var profile = WriteProfile();

        var exit = await Run("--iso", Iso, "--output", Output, "--profile", profile);

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("modules.fake.file", _out.ToString());
        Assert.DoesNotContain("modules.off.file", _out.ToString());
        Assert.Empty(_disk.Mounted);
    }

    [Fact]
    public async Task An_unreadable_input_folder_prints_it_and_exits_2_without_mounting()
    {
        _enabled.Inputs.Add(new ExternalInput("modules.fake.dir", @"C:\inputs\dir", ExternalInputKind.Folder));
        _fs.SeedDirectory(@"C:\inputs\dir");
        _fs.SeedDirectory(@"C:\inputs\dir\locked");
        _fs.MarkUnreadable(@"C:\inputs\dir\locked");
        var profile = WriteProfile();

        var exit = await Run("--iso", Iso, "--output", Output, "--profile", profile);

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains(@"C:\inputs\dir\locked", _out.ToString());
        Assert.Empty(_disk.Mounted);
    }

    [Fact]
    public async Task Without_a_profile_it_writes_a_plain_answer_file_and_the_reminder()
    {
        var exit = await RunBuild();

        Assert.Equal(ExitCodes.Ok, exit);
        var xml = System.Text.Encoding.UTF8.GetString(_fs.ReadAllBytes(Output + @"\autounattend.xml"));
        Assert.DoesNotContain("FirstLogonCommands", xml);
        Assert.True(_fs.FileExists(Output + @"\sources\boot.wim"));
        Assert.Contains(@"The installation media is ready in C:\out.", _out.ToString());
        Assert.Contains("formatkit prepare check", _out.ToString());
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task The_answer_file_is_logged_in_full_at_information_level()
    {
        await RunBuild();

        var entry = Assert.Single(_log.Events, e => e.RenderMessage().Contains("autounattend.xml:"));
        Assert.Equal(Serilog.Events.LogEventLevel.Information, entry.Level);
        Assert.Contains("<unattend", entry.RenderMessage());
    }

    [Fact]
    public async Task With_a_profile_it_writes_the_bundle_and_the_first_logon_commands()
    {
        _enabled.Inputs.Add(new ExternalInput("modules.fake.file", @"C:\inputs\x.json", ExternalInputKind.File));
        _fs.Seed(@"C:\inputs\x.json", "{}");
        var profile = WriteProfile("""{"schemaVersion":1,"name":"T","modules":{"fake":{"enabled":true,"file":"x"}}}""");

        var exit = await Run("--iso", Iso, "--output", Output, "--profile", profile);

        Assert.True(exit == ExitCodes.Ok, _out.ToString());
        var folder = Output + @"\sources\$OEM$\$1\FormatKit";
        Assert.True(_fs.FileExists(folder + @"\formatkit.exe"));
        Assert.True(_fs.FileExists(folder + @"\profile.json"));
        var xml = System.Text.Encoding.UTF8.GetString(_fs.ReadAllBytes(Output + @"\autounattend.xml"));
        Assert.Contains("FirstLogonCommands", xml);
    }

    [Fact]
    public async Task Progress_prints_each_phase_and_a_percentage_line_per_ten_percent()
    {
        await RunBuild();

        var text = _out.ToString();
        Assert.Contains("Copying the Windows files", text);
        Assert.Contains("Writing autounattend.xml", text);
        Assert.Contains("100 %", text);
        var percents = System.Text.RegularExpressions.Regex.Matches(text, @"^  (\d+) %", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(percents.Order().Distinct(), percents);
        Assert.All(percents, p => Assert.Equal(0, p % 10));
    }

    [Fact]
    public async Task A_split_failure_says_the_build_is_incomplete_exits_1_and_dismounts()
    {
        _fs.SeedEntry(@"E:\sources\install.wim", 5_000_000_000);
        _images.ThrowOnSplit = new InvalidOperationException("dism failed");

        var exit = await RunBuild();

        Assert.Equal(ExitCodes.CompletedWithFailures, exit);
        Assert.Contains("is incomplete", _out.ToString());
        Assert.Contains("dism failed", _out.ToString());
        Assert.Contains("Delete the folder", _out.ToString());
        AssertNothingLoggedToTheConsole();
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task Ctrl_c_during_the_copy_exits_130_and_dismounts()
    {
        using var cts = new CancellationTokenSource();
        _diskImages = new CancelOnMount(_disk, cts);

        var exit = await Run(cts.Token, "--iso", Iso, "--output", Output);

        Assert.Equal(ExitCodes.Cancelled, exit);
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task A_media_exception_is_printed_and_exits_2()
    {
        _images.Images[@"E:\sources\install.wim"] =
            [new WindowsImageInfo(1, "Windows 11 Pro", 12, "10.0.26100.1742", ["en-US"], 0)];

        var exit = await RunBuild();

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("Windows 11 Pro", _out.ToString());
        Assert.Equal([Iso], _disk.Dismounted);
    }

    // ---- media usb ----

    private static readonly string Size32 = string.Create(CultureInfo.CurrentCulture, $"{32.0:0.0} GB");

    private static DiskInfo Stick(int number = 3, string uniqueId = "UID-3", long size = 32L << 30, bool removable = true,
        string bus = "USB", bool boot = false, string name = "SanDisk Ultra", params DiskVolume[] volumes) =>
        new(number, uniqueId, name, size, bus, removable, boot, false,
            volumes.Length > 0 ? volumes : [new DiskVolume('F', "BACKUP"), new DiskVolume('G', "DATA")]);

    /// <summary>Runs media usb and enforces Review Focus 2 on every case: a disk is prepared only after the typed number.</summary>
    private async Task<int> RunUsb(string?[] answers, params string[] args)
    {
        _answers = new Queue<string?>(answers);
        var exit = await RunVerb("usb", CancellationToken.None, ["--iso", Iso, .. args]);
        if (_disks.Prepared.Count > 0)
            Assert.Contains(_typed, a => a is not null && a.Trim() == "3");
        return exit;
    }

    [Fact]
    public async Task Usb_without_disk_lists_the_usb_disks_with_their_reasons_and_exits_2()
    {
        _disks.Disks.Add(Stick());
        _disks.Disks.Add(Stick(0, "UID-0", bus: "NVMe", boot: true, name: "Samsung 990"));
        _disks.Disks.Add(Stick(5, "UID-5", removable: false, name: "WD Elements"));
        _disks.Disks.Add(Stick(6, "UID-6", size: 100, name: "Tiny Stick"));

        var exit = await RunUsb([]);

        Assert.Equal(ExitCodes.InvalidInput, exit);
        var text = _out.ToString();
        Assert.Contains($"Disk 3 - SanDisk Ultra {Size32}", text);
        Assert.Contains("F: BACKUP", text);
        Assert.DoesNotContain("WD Elements", text);
        Assert.Contains("external hard disk", text);
        Assert.Contains("--allow-hard-disk", text);
        Assert.Contains("too small", text);
        Assert.DoesNotContain("Samsung 990", text);
        Assert.DoesNotContain("HardDisk", text);
        Assert.Empty(_disks.Prepared);
        Assert.Equal(0, _readLineCalls);
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task Usb_without_disk_and_no_usb_disks_says_so()
    {
        _disks.Disks.Add(Stick(0, "UID-0", bus: "NVMe", boot: true));

        var exit = await RunUsb([]);

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("No USB disk", _out.ToString());
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Theory]
    [InlineData("NVMe", false, true, 32L << 30, "not on the USB bus")]
    [InlineData("USB", true, true, 32L << 30, "boot or system disk")]
    [InlineData("USB", false, false, 32L << 30, "--allow-hard-disk")]
    [InlineData("USB", false, true, 100L, "too small")]
    public async Task Usb_with_an_ineligible_disk_prints_the_localized_reason_and_exits_2(
        string bus, bool boot, bool removable, long size, string expected)
    {
        _disks.Disks.Add(Stick(bus: bus, boot: boot, removable: removable, size: size));

        var exit = await RunUsb(["3"], "--disk", "3");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("Disk 3 cannot be used", _out.ToString());
        Assert.Contains(expected, _out.ToString());
        Assert.DoesNotContain("NotUsb", _out.ToString());
        Assert.DoesNotContain("BootOrSystem", _out.ToString());
        Assert.Empty(_disks.Prepared);
        Assert.Equal(0, _readLineCalls);
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task Usb_refuses_a_disk_that_holds_the_iso()
    {
        _disks.Disks.Add(Stick(volumes: new DiskVolume('C', "ISOS")));

        var exit = await RunUsb(["3"], "--disk", "3");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("Copy them to another drive first", _out.ToString());
        Assert.Contains(Iso, _out.ToString());
        Assert.Empty(_disks.Prepared);
        Assert.Equal(0, _readLineCalls);
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task Usb_refuses_a_disk_that_holds_the_profile()
    {
        var profile = WriteProfile();
        _disks.Disks.Add(Stick(volumes: new DiskVolume(char.ToUpperInvariant(profile[0]), "PROFILE")));

        var exit = await RunUsb(["3"], "--disk", "3", "--profile", profile);

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains(profile, _out.ToString());
        Assert.Empty(_disks.Prepared);
    }

    [Fact]
    public async Task Usb_refuses_a_disk_that_holds_the_running_exe()
    {
        // formatkit.exe started from the stick it is about to erase.
        const string exe = @"H:\tools\formatkit.exe";
        _fs.Seed(exe, "EXE");
        MediaCommands.ExePath = () => exe;
        _disks.Disks.Add(Stick(volumes: new DiskVolume('H', "STICK")));

        var exit = await RunUsb(["3"], "--disk", "3");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("Copy them to another drive first", _out.ToString());
        Assert.Contains(exe, _out.ToString());
        Assert.Empty(_disks.Prepared);
        Assert.Equal(0, _readLineCalls);
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task Usb_refuses_a_disk_without_a_unique_id_before_the_prompt()
    {
        _disks.Disks.Add(Stick(uniqueId: " "));

        var exit = await RunUsb(["3"], "--disk", "3");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("Disk 3 cannot be used", _out.ToString());
        Assert.Contains("cannot be told apart", _out.ToString());
        Assert.Empty(_disks.Prepared);
        Assert.Equal(0, _readLineCalls);
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task Usb_with_an_unknown_disk_number_exits_2()
    {
        _disks.Disks.Add(Stick());

        var exit = await RunUsb(["9"], "--disk", "9");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("Disk 9 was not found", _out.ToString());
        Assert.Empty(_disks.Prepared);
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task Usb_shows_the_disk_and_the_warning_and_writes_after_the_typed_number()
    {
        _disks.Disks.Add(Stick());

        var exit = await RunUsb(["3"], "--disk", "3");

        Assert.True(exit == ExitCodes.Ok, _out.ToString());
        var text = _out.ToString();
        Assert.Contains($"Disk 3 - SanDisk Ultra {Size32} (USB, removable)", text);
        Assert.Contains("F: BACKUP", text);
        Assert.Contains("G: DATA", text);
        Assert.Contains("EVERYTHING on it will be erased", text);
        Assert.DoesNotContain("external hard disk", text);
        Assert.Contains("The USB stick is ready", text);
        Assert.Equal([(3, "UID-3")], _disks.Prepared);
        Assert.True(_fs.FileExists(@"F:\autounattend.xml"));
        Assert.True(_fs.FileExists(@"F:\sources\boot.wim"));
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Theory]
    [InlineData(" 3 ")]
    [InlineData("3\t")]
    public async Task Usb_trims_whitespace_around_the_typed_number(string answer)
    {
        _disks.Disks.Add(Stick());

        var exit = await RunUsb([answer], "--disk", "3");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Single(_disks.Prepared);
    }

    [Theory]
    [InlineData("y")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("03")]
    [InlineData("disk 3")]
    [InlineData("3 3")]
    [InlineData("4")]
    public async Task Never_prepares_without_typed_number(string? answer)
    {
        _disks.Disks.Add(Stick());

        var exit = await RunUsb([answer], "--disk", "3");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("Cancelled", _out.ToString());
        Assert.Empty(_disks.Prepared);
        Assert.Equal(1, _readLineCalls);
        Assert.False(_fs.FileExists(@"F:\autounattend.xml"));
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task Usb_for_an_external_hard_disk_with_the_flag_adds_the_hard_disk_warning()
    {
        _disks.Disks.Add(Stick(removable: false));

        var exit = await RunUsb(["3"], "--disk", "3", "--allow-hard-disk");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains("This is an external hard disk.", _out.ToString());
        Assert.Single(_disks.Prepared);
    }

    [Fact]
    public async Task Usb_without_a_console_refuses_without_reading_a_line()
    {
        _console.CanPrompt = false;
        _disks.Disks.Add(Stick());

        var exit = await RunUsb(["3"], "--disk", "3");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("needs a console", _out.ToString());
        Assert.Equal(0, _readLineCalls);
        Assert.Empty(_disks.Prepared);
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task A_disk_swapped_after_the_listing_is_not_erased_and_the_message_is_localized()
    {
        _disks.Disks.Add(Stick());
        _disks.BeforeSecondList = () => _disks.Disks[0] = Stick(uniqueId: "OTHER");

        var exit = await RunUsb(["3"], "--disk", "3");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("Nothing was erased", _out.ToString());
        Assert.DoesNotContain("confirmed", _out.ToString());
        Assert.Empty(_disks.Prepared);
        Assert.False(_fs.FileExists(@"F:\autounattend.xml"));
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task A_disk_unplugged_after_the_listing_is_not_erased()
    {
        _disks.Disks.Add(Stick());
        _disks.BeforeSecondList = () => _disks.Disks.Clear();

        var exit = await RunUsb(["3"], "--disk", "3");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("Nothing was erased", _out.ToString());
        Assert.DoesNotContain("no longer connected", _out.ToString());
        AssertNothingLoggedToTheConsole();
        Assert.Empty(_disks.Prepared);
    }

    [Fact]
    public async Task A_write_failure_after_the_erase_says_the_stick_is_incomplete_and_exits_1()
    {
        _disks.Disks.Add(Stick());
        _fs.SeedEntry(@"E:\sources\install.wim", 5_000_000_000);
        _images.ThrowOnSplit = new InvalidOperationException("dism failed");

        var exit = await RunUsb(["3"], "--disk", "3");

        Assert.Equal(ExitCodes.CompletedWithFailures, exit);
        Assert.Contains("incomplete", _out.ToString());
        Assert.Contains("Run the command again", _out.ToString());
        Assert.Contains("dism failed", _out.ToString());
        Assert.Single(_disks.Prepared);
        AssertNothingLoggedToTheConsole();
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task Usb_listing_with_only_an_external_hard_disk_says_no_disks_and_keeps_the_hint()
    {
        _disks.Disks.Add(Stick(5, "UID-5", removable: false, name: "WD Elements"));

        var exit = await RunUsb([]);

        Assert.Equal(ExitCodes.InvalidInput, exit);
        var text = _out.ToString();
        Assert.Contains("No USB disk", text);
        Assert.Contains("hidden", text);
        Assert.Contains("--allow-hard-disk", text);
        Assert.DoesNotContain("WD Elements", text);
    }

    [Fact]
    public async Task Usb_listing_with_the_flag_shows_the_external_hard_disk()
    {
        _disks.Disks.Add(Stick(5, "UID-5", removable: false, name: "WD Elements"));

        var exit = await RunUsb([], "--allow-hard-disk");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("Disk 5 - WD Elements", _out.ToString());
        Assert.DoesNotContain("hidden", _out.ToString());
    }

    [Fact]
    public async Task A_failure_while_erasing_says_the_stick_is_incomplete_and_exits_1()
    {
        _disks.Disks.Add(Stick());
        _disks.ThrowOnPrepare = new InvalidOperationException("diskpart exploded");

        var exit = await RunUsb(["3"], "--disk", "3");

        Assert.Equal(ExitCodes.CompletedWithFailures, exit);
        Assert.Contains("is incomplete", _out.ToString());
        Assert.Contains("Run the command again", _out.ToString());
        Assert.DoesNotContain("diskpart exploded", _out.ToString());
        AssertNothingLoggedToTheConsole();
        Assert.Empty(_disks.Prepared);
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task Ctrl_c_while_erasing_says_incomplete_interrupted_and_exits_130()
    {
        _disks.Disks.Add(Stick());
        _disks.ThrowOnPrepare = new OperationCanceledException();

        var exit = await RunUsb(["3"], "--disk", "3");

        Assert.Equal(ExitCodes.Cancelled, exit);
        Assert.Contains("is incomplete", _out.ToString());
        Assert.Contains("interrupted", _out.ToString());
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task Ctrl_c_while_writing_after_the_erase_says_incomplete_interrupted_and_exits_130()
    {
        _disks.Disks.Add(Stick());
        _fs.SeedEntry(@"E:\sources\install.wim", 5_000_000_000);
        _images.ThrowOnSplit = new OperationCanceledException();

        var exit = await RunUsb(["3"], "--disk", "3");

        Assert.Equal(ExitCodes.Cancelled, exit);
        Assert.Contains("is incomplete", _out.ToString());
        Assert.Contains("interrupted", _out.ToString());
        Assert.Single(_disks.Prepared);
        Assert.Equal([Iso], _disk.Dismounted);
    }

    [Fact]
    public async Task Usb_with_declined_elevation_exits_5_without_mounting_or_erasing()
    {
        _elevation.IsElevated = false;
        _elevation.RelaunchResult = null;
        _disks.Disks.Add(Stick());

        var exit = await RunUsb(["3"], "--disk", "3");

        Assert.Equal(ExitCodes.ElevationDeclined, exit);
        Assert.Empty(_disk.Mounted);
        Assert.Empty(_disks.Prepared);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    public void Every_usb_refusal_has_a_localized_string(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        foreach (var refusal in Enum.GetValues<FormatKit.Media.Usb.UsbRefusal>().Where(r => r != FormatKit.Media.Usb.UsbRefusal.None))
        {
            var key = "Media_UsbRefusal_" + refusal;
            var text = FormatKit.Cli.Localization.Strings.Get(key);
            Assert.NotEqual(key, text);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Fact]
    public async Task Usb_not_elevated_relaunches_before_mounting()
    {
        _elevation.IsElevated = false;
        _elevation.RelaunchResult = 0;
        _disks.Disks.Add(Stick());

        var exit = await RunUsb(["3"], "--disk", "3");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal(1, _elevation.RelaunchCalls);
        Assert.Empty(_disk.Mounted);
        Assert.Empty(_disks.Prepared);
    }

    [Fact]
    public void AbsolutizePaths_makes_iso_absolute()
    {
        var args = CliApp.AbsolutizePaths(["media", "build", "--iso", "w.iso", "--iso=v.iso"]);

        Assert.True(Path.IsPathRooted(args[3]));
        Assert.True(Path.IsPathRooted(args[4]["--iso=".Length..]));
    }

    private sealed class CancelOnMount(FakeDiskImages inner, CancellationTokenSource cts) : IDiskImages
    {
        public Task<string> MountAsync(string isoPath, CancellationToken ct)
        {
            cts.Cancel();
            return inner.MountAsync(isoPath, CancellationToken.None);
        }

        public Task DismountAsync(string isoPath, CancellationToken ct) => inner.DismountAsync(isoPath, ct);
    }

    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        public List<Serilog.Events.LogEvent> Events { get; } = [];

        public void Emit(Serilog.Events.LogEvent logEvent) => Events.Add(logEvent);
    }

    // The console sink shows Warning and above (LogSetup): a handled failure that already printed a
    // localized message must log below that level, or its English text appears on screen too.
    private void AssertNothingLoggedToTheConsole() =>
        Assert.DoesNotContain(_log.Events, e => e.Level >= Serilog.Events.LogEventLevel.Warning);
}
