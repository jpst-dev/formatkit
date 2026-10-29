using FormatKit.App.Services;
using FormatKit.App.Tests.Fakes;
using FormatKit.App.ViewModels;
using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Hosting;

namespace FormatKit.App.Tests;

public sealed class RunProfileReviewTests : IDisposable
{
    private const string SettingsPath = @"C:\Users\x\AppData\Local\FormatKit\app.json";

    private const string ValidJson =
        """{"schemaVersion":1,"name":"Mine","modules":{"drivers":{"enabled":true},"programs":{"enabled":true}}}""";

    private readonly string _dir = Directory.CreateTempSubdirectory("fk-run-").FullName;
    private readonly InMemoryFileSystem _files = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeModule _drivers = new("drivers");
    private readonly FakeModule _programs = new("programs");

    public RunProfileReviewTests()
    {
        _drivers.Steps.Add(new FakeStep("drivers.scan") { Title = "Scan drivers", RequiresAdmin = true });
        _programs.Steps.Add(new FakeStep("programs.winget") { Title = "Install programs" });
        _programs.Steps.Add(new FakeStep("programs.direct") { Title = "Direct installs" });
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private RunProfileViewModel Create(out AppSettings settings)
    {
        IReadOnlyList<IModule> modules = [_drivers, _programs];
        var env = new HostEnvironment(
            TestServices.Create(files: _files),
            new Elevation(),
            new FakeScheduledTasks(),
            new FakeRebooter(),
            new FakeReportWriter(),
            new RunStateStore(_files, new AppPaths(@"C:\FormatKitTest")),
            new Serilog.LoggerConfiguration().CreateLogger(),
            TimeSpan.Zero,
            TimeSpan.Zero);
        settings = new AppSettings(_files, SettingsPath);
        return new RunProfileViewModel(
            new ProfileService(modules), modules, new NoLauncher(), new Elevation(), _dialogs, settings, env);
    }

    private RunProfileViewModel Create() => Create(out _);

    private string WriteProfile(string json)
    {
        var path = Path.Combine(_dir, "profile.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Open_Valid_ShowsPlanMatchingDryRun()
    {
        var vm = Create();
        var path = WriteProfile(ValidJson);
        _dialogs.OpenFileResult = path;

        vm.OpenCommand.Execute(null);

        var loaded = new ProfileService([_drivers, _programs]).Load(path).Profile!;
        var expected = PlanService.Build(loaded.Profile, [_drivers, _programs], null);
        Assert.Equal(RunPhase.Review, vm.Phase);
        Assert.Empty(vm.Errors);
        Assert.Equal(expected.Select(s => s.Id), vm.Plan.Select(r => r.Id));
        Assert.Equal(["drivers.scan", "programs.winget", "programs.direct"], vm.Plan.Select(r => r.Id));
        Assert.Equal(["Scan drivers", "Install programs", "Direct installs"], vm.Plan.Select(r => r.Title));
        Assert.Equal([true, false, false], vm.Plan.Select(r => r.RequiresAdmin));
        Assert.Equal(loaded.Hash, vm.ReviewedHash);
        Assert.Equal(path, vm.ProfilePath);
    }

    [Fact]
    public void Open_Invalid_ShowsErrors()
    {
        var vm = Create();
        _dialogs.OpenFileResult = WriteProfile("""{"schemaVersion":1,"name":"Mine","modules":{"nope":{"enabled":true}}}""");

        vm.OpenCommand.Execute(null);

        Assert.Equal(RunPhase.Review, vm.Phase);
        Assert.NotEmpty(vm.Errors);
        Assert.Empty(vm.Plan);
        Assert.Null(vm.ReviewedHash);
    }

    [Fact]
    public void Open_Cancelled_ChangesNothing()
    {
        var vm = Create();
        _dialogs.OpenFileResult = null;

        vm.OpenCommand.Execute(null);

        Assert.Equal(RunPhase.Choose, vm.Phase);
        Assert.Null(vm.ProfilePath);
    }

    [Fact]
    public void Open_RemembersLastProfile()
    {
        var vm = Create(out var settings);
        var path = WriteProfile(ValidJson);
        _dialogs.OpenFileResult = path;

        vm.OpenCommand.Execute(null);

        Assert.Equal(path, settings.LastProfile);
        Assert.Equal(path, new AppSettings(_files, SettingsPath).LastProfile);
    }

    [Fact]
    public void LastProfile_Existing_IsOffered()
    {
        var path = WriteProfile(ValidJson);
        _files.Seed(path, ValidJson);
        new AppSettings(_files, SettingsPath).LastProfile = path;

        var vm = Create();
        vm.UseLastProfileCommand.Execute(null);

        Assert.Equal(path, vm.LastProfile);
        Assert.Equal(RunPhase.Review, vm.Phase);
        Assert.Equal(3, vm.Plan.Count);
    }

    [Fact]
    public void LastProfileMissing_NotOffered()
    {
        new AppSettings(_files, SettingsPath).LastProfile = Path.Combine(_dir, "gone.json");

        var vm = Create();

        Assert.Null(vm.LastProfile);
        Assert.False(vm.UseLastProfileCommand.CanExecute(null));
    }

    [Fact]
    public void DriversOnly_ShowsDriverSteps()
    {
        var vm = Create();

        vm.UseDriversOnlyCommand.Execute(null);

        Assert.Equal(RunPhase.Review, vm.Phase);
        Assert.Equal(["drivers.scan"], vm.Plan.Select(r => r.Id));
        Assert.Equal(BuiltInProfiles.DriversOnlyHash, vm.ReviewedHash);
        Assert.Equal(BuiltInProfiles.DriversOnlyPath, vm.ProfilePath);
    }

    [Fact]
    public void Reload_ReadsFileAgain()
    {
        var vm = Create();
        var path = WriteProfile(ValidJson);
        _dialogs.OpenFileResult = path;
        vm.OpenCommand.Execute(null);
        var firstHash = vm.ReviewedHash;

        File.WriteAllText(path, """{"schemaVersion":1,"name":"Mine","modules":{"drivers":{"enabled":true}}}""");
        vm.ReloadCommand.Execute(null);

        Assert.Equal(["drivers.scan"], vm.Plan.Select(r => r.Id));
        Assert.NotEqual(firstHash, vm.ReviewedHash);
    }

    [Fact]
    public void Reload_FileBecomesInvalid_ShowsErrorsAndDropsPlan()
    {
        var vm = Create();
        var path = WriteProfile(ValidJson);
        _dialogs.OpenFileResult = path;
        vm.OpenCommand.Execute(null);

        File.WriteAllText(path, "{ broken");
        vm.ReloadCommand.Execute(null);

        Assert.NotEmpty(vm.Errors);
        Assert.Empty(vm.Plan);
        Assert.Null(vm.ReviewedHash);
    }
}
