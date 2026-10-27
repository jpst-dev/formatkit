using System.Globalization;
using System.Management;
using FormatKit.Cli.Tests.Fakes;
using FormatKit.Core;
using FormatKit.Core.Hardware;
using FormatKit.Core.Profiles;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Drivers;
using FormatKit.Drivers.Catalog;

namespace FormatKit.Cli.Tests;

[Collection("Culture")]
public sealed class CliAppTests : IDisposable
{
    private readonly CultureInfo _original = CultureInfo.CurrentUICulture;
    private readonly string _tempDir = Directory.CreateTempSubdirectory("formatkit-cli-").FullName;
    private readonly AppPaths _paths = new(Path.Combine("data", "FormatKit"));
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeElevation _elevation = new();
    private readonly FakeScheduledTasks _tasks = new();
    private readonly FakeRebooter _rebooter = new();
    private readonly FakeReportWriter _reports = new();
    private readonly FakeModule _module = new("fake");
    private readonly StringWriter _out = new();
    private FakeHardwareInfo _hardware = new(FakeHardwareInfo.Sample());

    public CliAppTests() => CultureInfo.CurrentUICulture = new CultureInfo("en-US");

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _original;
        Directory.Delete(_tempDir, recursive: true);
    }

    private CliApp App(IHardwareInfo? hardware = null)
    {
        var services = TestServices.Create(files: _fs);
        if (hardware is not null)
            services = services with { Hardware = hardware };
        else
            services = services with { Hardware = _hardware };

        var env = new CliEnvironment(
            services, _elevation, _tasks, _rebooter, _reports,
            new RunStateStore(_fs, _paths), Serilog.Core.Logger.None, _out,
            TimeSpan.Zero, TimeSpan.Zero);
        return new CliApp(env, VendorCatalog.LoadEmbedded(), [_module]);
    }

    private Task<int> Run(params string[] args) => App().RunAsync(args, CancellationToken.None);

    private string WriteProfile()
    {
        var path = Path.Combine(_tempDir, "fake.json");
        File.WriteAllText(path, """{"schemaVersion":1,"name":"T","modules":{"fake":{"enabled":true}}}""");
        return path;
    }

    private static string ExampleProfilePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "profiles", "example.json");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("profiles/example.json not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public async Task Invalid_profile_prints_errors_and_returns_InvalidInput()
    {
        var missing = Path.Combine(_tempDir, "nope.json");

        var exit = await Run("run", "--profile", missing);

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains(missing, _out.ToString());
        Assert.Equal(0, _fs.WriteCount);
    }

    [Fact]
    public async Task Dry_run_prints_plan_without_elevating()
    {
        var services = TestServices.Create(files: _fs);
        var env = new CliEnvironment(
            services, _elevation, _tasks, _rebooter, _reports,
            new RunStateStore(_fs, _paths), Serilog.Core.Logger.None, _out, TimeSpan.Zero, TimeSpan.Zero);

        var exit = await new CliApp(env).RunAsync(["run", "--profile", ExampleProfilePath(), "--dry-run"], CancellationToken.None);

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains("drivers.network-check", _out.ToString());
        Assert.Equal(0, _elevation.RelaunchCalls);
        Assert.Equal(0, _fs.WriteCount);
    }

    [Fact]
    public async Task Drivers_install_dry_run_prints_builtin_plan_without_elevating()
    {
        var exit = await App().RunAsync(["drivers", "install", "--dry-run"], CancellationToken.None);

        // The fake module list has no "drivers" module, so the built-in profile yields an empty plan.
        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal(0, _elevation.RelaunchCalls);
    }

    [Fact]
    public async Task Declined_elevation_returns_ElevationDeclined()
    {
        _module.Steps.Add(new FakeStep("a") { RequiresAdmin = true, CheckResult = false });
        _elevation.IsElevated = false;
        _elevation.RelaunchResult = null;

        var exit = await Run("run", "--profile", WriteProfile());

        Assert.Equal(ExitCodes.ElevationDeclined, exit);
        Assert.Equal(1, _elevation.RelaunchCalls);
        Assert.Equal(0, _fs.WriteCount);
        Assert.Contains("administrator permission was declined", _out.ToString());
    }

    [Fact]
    public async Task Relaunched_child_exit_code_is_returned()
    {
        _module.Steps.Add(new FakeStep("a") { RequiresAdmin = true });
        _elevation.RelaunchResult = 4;

        var exit = await Run("run", "--profile", WriteProfile());

        Assert.Equal(4, exit);
        Assert.Equal(0, _fs.WriteCount);
    }

    [Fact]
    public async Task Elevated_run_maps_completed_to_zero()
    {
        _module.Steps.Add(new FakeStep("a") { RequiresAdmin = true, Title = "Step A", CheckResult = false });
        _elevation.IsElevated = true;

        var exit = await Run("run", "--profile", WriteProfile());

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal(0, _elevation.RelaunchCalls);
        Assert.Contains("Step A", _out.ToString());
        Assert.Contains(FakeReportWriter.Directory, _out.ToString());
    }

    [Fact]
    public async Task Failures_map_to_one()
    {
        _module.Steps.Add(new FakeStep("a") { CheckResult = false, ExecuteResult = StepResult.Fail("do it by hand", "boom") });
        _elevation.IsElevated = true;

        var exit = await Run("run", "--profile", WriteProfile());

        Assert.Equal(ExitCodes.CompletedWithFailures, exit);
        Assert.Contains("do it by hand", _out.ToString());
    }

    [Fact]
    public async Task Critical_failure_maps_to_three()
    {
        _module.Steps.Add(new FakeStep("a") { IsCritical = true, CheckResult = false, ExecuteResult = StepResult.Fail(null, "boom") });
        _elevation.IsElevated = true;

        var exit = await Run("run", "--profile", WriteProfile());

        Assert.Equal(ExitCodes.StoppedOnCriticalFailure, exit);
    }

    [Fact]
    public async Task Reboot_pending_maps_to_four_and_explains_automatic_resume()
    {
        _module.Steps.Add(new FakeStep("a") { CheckResult = false, ExecuteResult = StepResult.Reboot("restart") });
        _elevation.IsElevated = true;
        _rebooter.Result = false; // the user cancelled the countdown

        var exit = await Run("run", "--profile", WriteProfile());

        Assert.Equal(ExitCodes.RebootPending, exit);
        Assert.Contains("resume automatically at the next logon", _out.ToString());
    }

    [Fact]
    public async Task Resume_without_state_returns_six()
    {
        var exit = await Run("run", "--resume");

        Assert.Equal(ExitCodes.NothingToResume, exit);
        Assert.Contains("no interrupted run", _out.ToString());
    }

    [Fact]
    public async Task Resume_with_corrupt_state_suggests_new_run()
    {
        _fs.Seed(_paths.StateFile, "{ not json");

        var exit = await Run("run", "--resume");

        Assert.Equal(ExitCodes.NothingToResume, exit);
        Assert.Contains("run --profile", _out.ToString());
    }

    [Fact]
    public async Task Resume_uses_the_profile_recorded_in_state()
    {
        var profile = WriteProfile();
        var step = new FakeStep("a") { CheckResult = false };
        _module.Steps.Add(step);
        _module.Steps.Add(new FakeStep("b") { CheckResult = false });
        _elevation.IsElevated = true;
        var hash = new ProfileLoader([_module]).Load(profile).Value!.Hash;
        var state = new RunState(profile, hash, ["a", "b"],
            new Dictionary<string, StepRecord> { ["a"] = new(StepOutcome.Succeeded, DateTimeOffset.UtcNow, [], null) },
            DateTimeOffset.UtcNow);
        new RunStateStore(_fs, _paths).Save(state);

        var exit = await Run("run", "--resume");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal(0, step.ExecuteCalls);
    }

    [Fact]
    public async Task Resume_with_changed_profile_returns_six()
    {
        var profile = WriteProfile();
        _module.Steps.Add(new FakeStep("a"));
        new RunStateStore(_fs, _paths).Save(new RunState(profile, "other-hash", ["a"],
            new Dictionary<string, StepRecord>(), DateTimeOffset.UtcNow));
        _elevation.IsElevated = true;

        var exit = await Run("run", "--resume");

        Assert.Equal(ExitCodes.NothingToResume, exit);
        Assert.Contains("profile changed", _out.ToString());
    }

    [Fact]
    public async Task Profile_and_resume_together_is_invalid_input()
    {
        var exit = await Run("run", "--profile", WriteProfile(), "--resume");

        Assert.Equal(ExitCodes.InvalidInput, exit);
    }

    [Fact]
    public async Task Unknown_command_is_invalid_input()
    {
        Assert.Equal(ExitCodes.InvalidInput, await Run("frobnicate"));
    }

    [Fact]
    public async Task Cancellation_during_run_prints_resume_hint()
    {
        _module.Steps.Add(new FakeStep("a") { CheckResult = false, Throws = new OperationCanceledException() });
        _elevation.IsElevated = true;

        var exit = await Run("run", "--profile", WriteProfile());

        Assert.Equal(ExitCodes.Cancelled, exit);
        Assert.Contains("run --resume", _out.ToString());
    }

    [Fact]
    public void Example_profile_is_valid()
    {
        var loader = new ProfileLoader([new DriversModule(VendorCatalog.LoadEmbedded(), TimeSpan.Zero)]);

        var result = loader.Load(ExampleProfilePath());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    [Fact]
    public async Task Hardware_prints_aligned_snapshot()
    {
        _hardware = new FakeHardwareInfo(FakeHardwareInfo.Sample(
            manufacturer: "Contoso", model: "Model 1",
            gpus: [new GpuInfo("Contoso GPU", "PCI\\VEN_1", GpuVendor.Unknown, "1.2.3")]));

        var exit = await Run("hardware");

        Assert.Equal(ExitCodes.Ok, exit);
        var text = _out.ToString();
        Assert.Contains("Machine:", text);
        Assert.Contains("Contoso Model 1", text);
        Assert.Contains("Contoso GPU (driver 1.2.3)", text);
        Assert.Contains("Problem devices:", text);
    }

    [Fact]
    public async Task Hardware_query_failure_prints_localized_error_without_stack_trace()
    {
        var exit = await App(new ThrowingHardware(new UnauthorizedAccessException("denied"))).RunAsync(["hardware"], CancellationToken.None);

        Assert.Equal(ExitCodes.CompletedWithFailures, exit);
        Assert.Contains("Could not read the system information: denied", _out.ToString());
        Assert.DoesNotContain("   at ", _out.ToString());
    }

    [Fact]
    public async Task Drivers_scan_prints_report_and_windows_update_error()
    {
        var wu = new ThrowingWu();
        var services = TestServices.Create(files: _fs) with { WindowsUpdate = wu } with { Hardware = _hardware };
        var env = new CliEnvironment(
            services, _elevation, _tasks, _rebooter, _reports,
            new RunStateStore(_fs, _paths), Serilog.Core.Logger.None, _out, TimeSpan.Zero, TimeSpan.Zero);

        var exit = await new CliApp(env, VendorCatalog.LoadEmbedded(), [_module]).RunAsync(["drivers", "scan"], CancellationToken.None);

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains("Machine type:", _out.ToString());
        Assert.Contains("Windows Update search failed: WU unavailable", _out.ToString());
    }

    [Fact]
    public async Task Output_is_localized_to_the_current_ui_culture()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");

        var exit = await Run("run", "--resume");

        Assert.Equal(ExitCodes.NothingToResume, exit);
        Assert.Contains("Não há execução interrompida", _out.ToString());
    }

    private sealed class ThrowingWu : FormatKit.Core.WindowsUpdate.IWindowsUpdateClient
    {
        public Task<IReadOnlyList<FormatKit.Core.WindowsUpdate.DriverUpdate>> SearchDriversAsync(CancellationToken ct) =>
            throw new InvalidOperationException("WU unavailable");

        public Task<FormatKit.Core.WindowsUpdate.WindowsUpdateInstallResult> InstallAsync(
            IReadOnlyList<FormatKit.Core.WindowsUpdate.DriverUpdate> updates, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingHardware(Exception ex) : IHardwareInfo
    {
        public Task<HardwareSnapshot> ReadAsync(CancellationToken ct) => throw ex;
    }
}
