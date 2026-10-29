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
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _original;
        MediaCommands.Machine = null;
        MediaCommands.ExePath = () => Environment.ProcessPath!;
        Directory.Delete(_tempDir, recursive: true);
    }

    private Task<int> Run(CancellationToken ct, params string[] args)
    {
        var services = TestServices.Create(files: _fs, diskImages: _disk, windowsImages: _images, virtualDisks: _virtualDisks) with { DiskImages = _diskImages };
        var env = new CliEnvironment(
            services, _elevation, new FakeScheduledTasks(), new FakeRebooter(), new FakeReportWriter(),
            new RunStateStore(_fs, new AppPaths("data")),
            new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_log).CreateLogger(), _out, TimeSpan.Zero, TimeSpan.Zero);
        return new CliApp(env, VendorCatalog.LoadEmbedded(), [_enabled, _disabled])
            .RunAsync(["media", "build", .. args], ct);
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
}
