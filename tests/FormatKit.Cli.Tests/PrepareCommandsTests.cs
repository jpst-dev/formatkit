using System.Globalization;
using FormatKit.Cli.Commands;
using FormatKit.Cli.Tests.Fakes;
using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Drivers.Catalog;
using FormatKit.Prepare;

namespace FormatKit.Cli.Tests;

[Collection("Culture")]
public sealed class PrepareCommandsTests : IDisposable
{
    private const string ValidProfile =
        """{"schemaVersion":1,"name":"T","modules":{"fake":{"enabled":true},"off":{"enabled":false}}}""";

    private readonly CultureInfo _original = CultureInfo.CurrentUICulture;
    private readonly string _tempDir = Directory.CreateTempSubdirectory("formatkit-cli-prepare-").FullName;
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeElevation _elevation = new() { IsElevated = true };
    private readonly FakeVolumes _volumes = new(new VolumeInfo('C', 0, BitLockerState.Off, null, null));
    private readonly FakeModule _enabled = new("fake");
    private readonly FakeModule _disabled = new("off");
    private readonly RecordingCheck _check = new();
    private readonly StringWriter _out = new();

    public PrepareCommandsTests()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        PrepareCommands.Checks = () => [_check];
        PrepareCommands.Environment = () =>
            new PrepareEnvironment(@"C:\Users\joao", @"C:\Users\joao\AppData\Local", @"C:\Users\joao\AppData\Roaming", 'C', "PC-TEST", s => s);
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _original;
        PrepareCommands.Checks = PrepareChecks.All;
        PrepareCommands.Environment = PrepareEnvironment.Current;
        Directory.Delete(_tempDir, recursive: true);
    }

    private Task<int> Run(params string[] args)
    {
        var services = TestServices.Create(files: _fs, volumes: _volumes);
        var env = new CliEnvironment(
            services, _elevation, new FakeScheduledTasks(), new FakeRebooter(), new FakeReportWriter(),
            new RunStateStore(_fs, new AppPaths("data")), Serilog.Core.Logger.None, _out, TimeSpan.Zero, TimeSpan.Zero);
        return new CliApp(env, VendorCatalog.LoadEmbedded(), [_enabled, _disabled])
            .RunAsync(["prepare", "check", .. args], CancellationToken.None);
    }

    private string WriteProfile(string json = ValidProfile)
    {
        var path = Path.Combine(_tempDir, "p.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static PrepareItem Item(PrepareStatus status) =>
        new("fake.check", PrepareGroup.Data, status, "Some title", "Some detail");

    [Fact]
    public async Task No_blocker_exits_0_and_the_summary_names_the_attention_items()
    {
        _check.Items = [Item(PrepareStatus.Attention)];

        var exit = await Run();

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.EndsWith("No blockers. Review the item marked Attention before formatting.", _out.ToString().TrimEnd());
        Assert.Contains("Some title", _out.ToString());
    }

    [Fact]
    public async Task Nothing_to_do_exits_0_and_prints_the_safe_summary()
    {
        _check.Items = [Item(PrepareStatus.Ok)];

        var exit = await Run();

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.EndsWith("Safe to format.", _out.ToString().TrimEnd());
    }

    [Fact]
    public async Task A_blocker_exits_1()
    {
        _check.Items = [Item(PrepareStatus.Blocker)];

        var exit = await Run();

        Assert.Equal(ExitCodes.CompletedWithFailures, exit);
        Assert.Contains("1 item blocks formatting.", _out.ToString());
    }

    [Fact]
    public async Task Invalid_profile_prints_errors_exits_2_and_runs_no_check()
    {
        var exit = await Run("--profile", WriteProfile("""{"schemaVersion":1,"name":"T","modules":{"nope":{}}}"""));

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("problem", _out.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _check.Calls);
    }

    [Fact]
    public async Task Valid_profile_hands_the_checks_the_inputs_of_enabled_modules_and_the_profile_file()
    {
        var wanted = new ExternalInput("$.fake.file", @"E:\x.json", ExternalInputKind.File);
        _enabled.Inputs.Add(wanted);
        _disabled.Inputs.Add(new ExternalInput("$.off.file", @"E:\off.json", ExternalInputKind.File));
        var profile = WriteProfile();

        var exit = await Run("--profile", profile);

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.NotNull(_check.Context!.Profile);
        Assert.Equal([wanted, new ExternalInput("", profile, ExternalInputKind.File)], _check.Context.Inputs);
    }

    [Fact]
    public async Task Without_a_profile_there_are_no_inputs()
    {
        await Run();

        Assert.Null(_check.Context!.Profile);
        Assert.Empty(_check.Context.Inputs);
    }

    [Fact]
    public async Task The_context_carries_volumes_and_elevation()
    {
        _volumes.Volumes.Add(new VolumeInfo('D', 1, BitLockerState.Off, @"C:\disks\dev.vhdx", null));
        _elevation.IsElevated = false;

        await Run();

        Assert.True(_check.Context!.Windows.IsLost(@"D:\code"));
        Assert.True(_check.Context.Windows.VolumesKnown);
        Assert.Equal(['C', 'D'], _check.Context.Windows.Volumes.Select(v => v.Letter));
        Assert.False(_check.Context.IsElevated);
    }

    [Fact]
    public async Task The_volumes_are_listed_once_per_run()
    {
        PrepareCommands.Checks = () =>
            [new FormatKit.Prepare.Checks.Security.BitLockerCheck(), new FormatKit.Prepare.Checks.Data.VolumesCheck(), _check];

        await Run();

        Assert.Equal(1, _volumes.Calls);
    }

    [Fact]
    public async Task A_volume_listing_failure_makes_other_drives_unknown()
    {
        _volumes.Throw = new IOException("no volumes");

        var exit = await Run();

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.False(_check.Context!.Windows.VolumesKnown);
        Assert.True(_check.Context.Windows.IsLost(@"C:\x"));
        Assert.Equal(PathLocation.Unknown, _check.Context.Windows.Locate(@"D:\x"));
    }

    [Fact]
    public async Task Output_writes_the_markdown_and_overwrites_an_existing_file()
    {
        _check.Items = [Item(PrepareStatus.Attention)];
        _fs.SeedDirectory(@"E:\");
        _fs.Seed(@"E:\c.md", "old");

        var exit = await Run("--output", @"E:\c.md");

        Assert.Equal(ExitCodes.Ok, exit);
        var written = _fs.ReadAllText(@"E:\c.md");
        Assert.Contains("# FormatKit preparation checklist", written);
        Assert.Contains("PC-TEST", written);
        Assert.Contains("Some title", written);
        Assert.Contains(@"Checklist saved to E:\c.md", _out.ToString());
        Assert.DoesNotContain("will be lost", _out.ToString());
    }

    [Fact]
    public async Task Output_on_the_windows_drive_warns_but_still_writes()
    {
        _fs.SeedDirectory(@"C:\fk");

        var exit = await Run("--output", @"C:\fk\c.md");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains("will be lost", _out.ToString());
        Assert.True(_fs.FileExists(@"C:\fk\c.md"));
    }

    [Fact]
    public async Task A_failed_output_write_prints_the_report_and_exits_2()
    {
        _check.Items = [Item(PrepareStatus.Blocker)];
        _fs.SeedDirectory(@"E:\");
        _fs.FailWrites = true;

        var exit = await Run("--output", @"E:\c.md");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("Some title", _out.ToString());
        Assert.Contains("Could not write", _out.ToString());
    }

    [Theory]
    [InlineData("argument")]
    [InlineData("notSupported")]
    [InlineData("security")]
    public async Task An_invalid_output_path_prints_the_report_and_exits_2(string error)
    {
        _check.Items = [Item(PrepareStatus.Blocker)];
        _fs.SeedDirectory(@"E:\");
        _fs.AtomicWriteError = error switch
        {
            "argument" => new ArgumentException("bad path"),
            "notSupported" => new NotSupportedException("bad format"),
            _ => new System.Security.SecurityException("denied"),
        };

        var exit = await Run("--output", @"E:\c.md");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("Some title", _out.ToString());
        Assert.Contains("Could not write", _out.ToString());
    }

    [Fact]
    public async Task An_output_path_that_cannot_be_resolved_exits_2_not_1()
    {
        // A NUL makes Path.GetFullPath throw while checking whether the file is on the Windows drive.
        _check.Items = [Item(PrepareStatus.Attention)];

        var exit = await Run("--output", "E:\\c\u0000.md");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("Some title", _out.ToString());
        Assert.Contains("Could not write", _out.ToString());
    }

    [Fact]
    public async Task Ctrl_c_exits_130_and_says_cancelled()
    {
        _check.Throw = new OperationCanceledException();

        var exit = await Run();

        Assert.Equal(ExitCodes.Cancelled, exit);
        Assert.Equal(130, exit);
        Assert.Contains("Cancelled.", _out.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Never_relaunches_elevated(bool elevated)
    {
        _elevation.IsElevated = elevated;

        await Run();

        Assert.Equal(0, _elevation.RelaunchCalls);
    }

    [Fact]
    public async Task Prints_in_Portuguese()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");

        await Run();

        Assert.Contains("É seguro formatar.", _out.ToString());
    }

    private sealed class RecordingCheck : IPrepareCheck
    {
        public string Id => "fake.check";
        public PrepareGroup Group => PrepareGroup.Data;
        public IReadOnlyList<PrepareItem> Items { get; set; } = [];
        public int Calls { get; private set; }
        public PrepareContext? Context { get; private set; }
        public Exception? Throw { get; set; }

        public Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
        {
            Calls++;
            Context = context;
            if (Throw is not null)
                throw Throw;
            return Task.FromResult(Items);
        }
    }
}
