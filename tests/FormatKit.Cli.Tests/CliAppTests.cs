using System.Globalization;
using System.Management;
using FormatKit.Cleanup;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cli.Tests.Fakes;
using FormatKit.Core;
using FormatKit.Core.Hardware;
using FormatKit.Core.Profiles;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Dev;
using FormatKit.Drivers;
using FormatKit.Drivers.Catalog;
using FormatKit.Programs;
using FormatKit.Programs.Catalog;

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
            TimeSpan.Zero, TimeSpan.Zero, _simulateRebootAfter);
        return new CliApp(env, VendorCatalog.LoadEmbedded(), [_module]);
    }

    private string? _simulateRebootAfter;

    [Fact]
    public async Task Test_switch_turns_the_named_step_into_a_reboot()
    {
        var a = new FakeStep("a") { Title = "Step A", CheckResult = false };
        var b = new FakeStep("b") { Title = "Step B", CheckResult = false };
        _module.Steps.Add(a);
        _module.Steps.Add(b);
        _elevation.IsElevated = true;
        _simulateRebootAfter = "a";

        var exit = await Run("run", "--profile", WriteProfile());

        Assert.Equal(ExitCodes.RebootPending, exit);
        Assert.Equal(1, a.ExecuteCalls);
        Assert.Equal(0, b.ExecuteCalls);
        Assert.Equal(1, _tasks.RegisterCalls);
        Assert.Single(_rebooter.Calls);
    }

    [Fact]
    public async Task Test_switch_for_an_unknown_step_changes_nothing()
    {
        _module.Steps.Add(new FakeStep("a") { CheckResult = false });
        _elevation.IsElevated = true;
        _simulateRebootAfter = "nope";

        var exit = await Run("run", "--profile", WriteProfile());

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Empty(_rebooter.Calls);
    }

    private Task<int> Run(params string[] args) => App().RunAsync(args, CancellationToken.None);

    private string WriteProfile()
    {
        var path = Path.Combine(_tempDir, "fake.json");
        File.WriteAllText(path, """{"schemaVersion":1,"name":"T","modules":{"fake":{"enabled":true}}}""");
        return path;
    }

    private static string ExampleProfilePath() => ProfilePath("example.json");

    private static string ProfilePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "profiles", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("profiles/" + fileName + " not found above " + AppContext.BaseDirectory);
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
        var services = TestServices.Create(files: _fs);
        var env = new CliEnvironment(
            services, _elevation, _tasks, _rebooter, _reports,
            new RunStateStore(_fs, _paths), Serilog.Core.Logger.None, _out, TimeSpan.Zero, TimeSpan.Zero);

        var exit = await new CliApp(env).RunAsync(["drivers", "install", "--dry-run"], CancellationToken.None);

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains("drivers.network-check", _out.ToString());
        Assert.Equal(0, _elevation.RelaunchCalls);
        Assert.Equal(0, _fs.WriteCount);
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
    public async Task Run_prints_each_step_as_it_starts()
    {
        _module.Steps.Add(new FakeStep("a") { Title = "Step A", CheckResult = false });
        _module.Steps.Add(new FakeStep("b") { Title = "Step B", CheckResult = false });
        _elevation.IsElevated = true;

        await Run("run", "--profile", WriteProfile());

        var output = _out.ToString();
        Assert.Contains("[1/2] Step A...", output);
        Assert.Contains("[2/2] Step B...", output);
        Assert.True(output.IndexOf("[1/2] Step A...", StringComparison.Ordinal) < output.IndexOf("[2/2]", StringComparison.Ordinal));
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
    public async Task Cancelled_hardware_query_returns_130()
    {
        var exit = await App(new ThrowingHardware(new OperationCanceledException())).RunAsync(["hardware"], CancellationToken.None);

        Assert.Equal(ExitCodes.Cancelled, exit);
        Assert.Contains("Cancelled", _out.ToString());
    }

    [Fact]
    public async Task Unexpected_exception_prints_one_line_error_without_stack_trace()
    {
        var exit = await App(new ThrowingHardware(new IOException("disk gone"))).RunAsync(["hardware"], CancellationToken.None);

        Assert.Equal(ExitCodes.CompletedWithFailures, exit);
        Assert.Contains("Unexpected error: disk gone", _out.ToString());
        Assert.DoesNotContain("   at ", _out.ToString());
    }

    [Fact]
    public async Task Relaunch_uses_absolute_paths_and_marks_the_elevated_child()
    {
        _module.Steps.Add(new FakeStep("a") { RequiresAdmin = true });
        _elevation.RelaunchResult = 3;
        var absolute = WriteProfile();
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, absolute);

        var exit = await Run("run", "--profile", relative);

        Assert.Equal(3, exit);
        var args = Assert.Single(_elevation.RelaunchArgs);
        Assert.Contains(absolute, args);
        Assert.Contains("--elevated-child", args);
        Assert.Contains("exit code 3", _out.ToString());
        Assert.Contains("critical failure", _out.ToString());
        Assert.Contains("reports", _out.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Saved_state_records_an_absolute_profile_path()
    {
        _module.Steps.Add(new FakeStep("a") { CheckResult = false, ExecuteResult = StepResult.Reboot("restart") });
        _elevation.IsElevated = true;
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, WriteProfile());

        await Run("run", "--profile", relative);

        var saved = new RunStateStore(_fs, _paths).Load().State!;
        Assert.True(Path.IsPathRooted(saved.ProfilePath));
    }

    [Fact]
    public async Task Elevated_child_waits_for_enter_except_when_rebooting()
    {
        _module.Steps.Add(new FakeStep("a") { CheckResult = false });
        _elevation.IsElevated = true;
        var waits = 0;
        var app = App();
        app.WaitForClose = () => { waits++; return Task.CompletedTask; };

        var exit = await app.RunAsync(["run", "--profile", WriteProfile(), "--elevated-child"], CancellationToken.None);

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal(1, waits);
        Assert.Contains("Press Enter to close", _out.ToString());

        _module.Steps.Clear();
        _module.Steps.Add(new FakeStep("r") { CheckResult = false, ExecuteResult = StepResult.Reboot("restart") });
        var app2 = App();
        app2.WaitForClose = () => { waits++; return Task.CompletedTask; };

        var exit2 = await app2.RunAsync(["run", "--profile", WriteProfile(), "--elevated-child"], CancellationToken.None);

        Assert.Equal(ExitCodes.RebootPending, exit2);
        Assert.Equal(1, waits);
    }

    [Fact]
    public async Task Resume_with_missing_profile_file_returns_six_with_hint()
    {
        new RunStateStore(_fs, _paths).Save(new RunState(Path.Combine(_tempDir, "gone.json"), "h", ["a"],
            new Dictionary<string, StepRecord>(), DateTimeOffset.UtcNow));

        var exit = await Run("run", "--resume");

        Assert.Equal(ExitCodes.NothingToResume, exit);
        Assert.Contains("run --profile", _out.ToString());
    }

    [Fact]
    public async Task Resume_without_state_removes_the_resume_task()
    {
        var exit = await Run("run", "--resume");

        Assert.Equal(ExitCodes.NothingToResume, exit);
        Assert.Equal(1, _tasks.RemoveCalls);
        Assert.Contains("automatic resume was removed", _out.ToString());
    }

    [Fact]
    public async Task Resume_without_state_and_without_task_does_not_claim_removal()
    {
        _tasks.TaskPresent = false;

        var exit = await Run("run", "--resume");

        Assert.Equal(ExitCodes.NothingToResume, exit);
        Assert.Equal(1, _tasks.RemoveCalls);
        Assert.DoesNotContain("automatic resume was removed", _out.ToString());
    }

    [Fact]
    public async Task Resume_with_corrupt_state_removes_the_resume_task()
    {
        _fs.Seed(_paths.StateFile, "{ not json");

        await Run("run", "--resume");

        Assert.Equal(1, _tasks.RemoveCalls);
    }

    [Fact]
    public async Task Resume_with_changed_profile_removes_the_resume_task()
    {
        var profile = WriteProfile();
        _module.Steps.Add(new FakeStep("a"));
        new RunStateStore(_fs, _paths).Save(new RunState(profile, "other-hash", ["a"],
            new Dictionary<string, StepRecord>(), DateTimeOffset.UtcNow));
        _elevation.IsElevated = true;

        await Run("run", "--resume");

        Assert.Equal(1, _tasks.RemoveCalls);
    }

    [Fact]
    public async Task Resume_with_invalid_profile_removes_the_resume_task()
    {
        new RunStateStore(_fs, _paths).Save(new RunState(Path.Combine(_tempDir, "gone.json"), "h", ["a"],
            new Dictionary<string, StepRecord>(), DateTimeOffset.UtcNow));

        await Run("run", "--resume");

        Assert.Equal(1, _tasks.RemoveCalls);
    }

    [Fact]
    public async Task Resume_early_exit_survives_a_failing_task_removal()
    {
        _tasks.RemoveException = new InvalidOperationException("schtasks broke");

        var exit = await Run("run", "--resume");

        Assert.Equal(ExitCodes.NothingToResume, exit);
        Assert.DoesNotContain("automatic resume was removed", _out.ToString());
    }

    [Fact]
    public async Task Reboot_with_failed_task_registration_still_returns_reboot_pending_with_manual_hint()
    {
        _module.Steps.Add(new FakeStep("a") { CheckResult = false, ExecuteResult = StepResult.Reboot("restart") });
        _elevation.IsElevated = true;
        _tasks.RegisterException = new InvalidOperationException("denied");

        var exit = await Run("run", "--profile", WriteProfile());

        Assert.Equal(ExitCodes.RebootPending, exit);
        Assert.Contains("Automatic resume could not be scheduled", _out.ToString());
        Assert.Contains("formatkit run --resume", _out.ToString());
        Assert.DoesNotContain("will resume automatically", _out.ToString());
    }

    [Fact]
    public async Task Wait_on_exit_waits_for_enter_except_when_rebooting()
    {
        _module.Steps.Add(new FakeStep("a") { CheckResult = false });
        _elevation.IsElevated = true;
        var waits = 0;
        var app = App();
        app.WaitForClose = () => { waits++; return Task.CompletedTask; };

        var exit = await app.RunAsync(["run", "--profile", WriteProfile(), "--wait-on-exit"], CancellationToken.None);

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal(1, waits);
        Assert.Contains("Press Enter to close", _out.ToString());

        _module.Steps.Clear();
        _module.Steps.Add(new FakeStep("r") { CheckResult = false, ExecuteResult = StepResult.Reboot("restart") });
        var app2 = App();
        app2.WaitForClose = () => { waits++; return Task.CompletedTask; };

        var exit2 = await app2.RunAsync(["run", "--profile", WriteProfile(), "--wait-on-exit"], CancellationToken.None);

        Assert.Equal(ExitCodes.RebootPending, exit2);
        Assert.Equal(1, waits);
    }

    [Fact]
    public async Task Without_wait_flags_the_window_does_not_wait()
    {
        _module.Steps.Add(new FakeStep("a") { CheckResult = false });
        _elevation.IsElevated = true;
        var waits = 0;
        var app = App();
        app.WaitForClose = () => { waits++; return Task.CompletedTask; };

        await app.RunAsync(["run", "--profile", WriteProfile()], CancellationToken.None);

        Assert.Equal(0, waits);
    }

    [Fact]
    public void Example_profile_is_valid()
    {
        var loader = new ProfileLoader([new DriversModule(VendorCatalog.LoadEmbedded(), TimeSpan.Zero)]);

        var result = loader.Load(ExampleProfilePath());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void Cleanup_balanced_profile_is_valid()
    {
        var loader = new ProfileLoader(
        [
            new DriversModule(VendorCatalog.LoadEmbedded(), TimeSpan.Zero),
            new CleanupModule(CleanupCatalog.LoadEmbedded(), _paths, _fs),
        ]);

        var result = loader.Load(ProfilePath("cleanup-balanced.json"));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void Programs_example_profile_is_valid()
    {
        var programs = new ProgramsModule(ProgramCatalog.LoadEmbedded(), _paths, _fs);
        var loader = new ProfileLoader([new DriversModule(VendorCatalog.LoadEmbedded(), TimeSpan.Zero), programs]);

        var result = loader.Load(ProfilePath("programs-example.json"));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));
        var section = result.Value!.Profile.Modules["programs"];
        Assert.True(programs.IsEnabled(section));
        var steps = programs.CreateSteps(section);
        Assert.NotEmpty(steps);
        Assert.All(steps, step => Assert.StartsWith("programs", step.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void Dev_example_profile_is_valid()
    {
        var dev = new DevModule(TimeProvider.System, DevEnvironment.Current() with { LogicalProcessors = 16 });
        var loader = new ProfileLoader(
        [
            new CleanupModule(CleanupCatalog.LoadEmbedded(), _paths, _fs),
            new ProgramsModule(ProgramCatalog.LoadEmbedded(), _paths, _fs),
            dev,
        ]);

        var result = loader.Load(ProfilePath("dev-example.json"));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));
        var profile = result.Value!.Profile;
        var steps = dev.CreateSteps(profile.Modules["dev"], profile.BaseDirectory);
        Assert.Equal(["dev.wsl", "dev.git", "dev.ssh", "dev.dotfiles", "dev.shell", "dev.devdrive"], steps.Select(st => st.Id));
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
        public Task<IReadOnlyList<ProblemDevice>> ReadDisabledDevicesAsync(CancellationToken ct) => throw ex;
        public Task<bool> HasBatteryAsync(CancellationToken ct) => throw ex;
    }
}
