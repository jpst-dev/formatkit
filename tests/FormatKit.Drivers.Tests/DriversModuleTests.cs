using System.Text.Json;
using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Drivers.Catalog;

namespace FormatKit.Drivers.Tests;

public class DriversModuleTests
{
    private static readonly DriversModule Module = new(VendorCatalog.LoadEmbedded(), TimeSpan.Zero);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static IReadOnlyList<ProfileError> Validate(string json) => Module.Validate(Json(json), "modules.drivers");

    [Fact]
    public void Valid_section_has_no_errors()
    {
        Assert.Empty(Validate("""{"enabled":true,"sources":["windowsUpdate","oem","gpu"],"networkDriverBackup":"D:\\drivers"}"""));
        Assert.Equal("drivers", Module.Name);
    }

    [Fact]
    public void Missing_enabled_is_error()
    {
        var error = Assert.Single(Validate("""{"sources":["gpu"]}"""));

        Assert.Equal(ProfileErrorCode.MissingField, error.Code);
        Assert.Equal("modules.drivers.enabled", error.Path);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void Unknown_source_is_error_with_index_path()
    {
        var error = Assert.Single(Validate("""{"enabled":true,"sources":["windowsUpdate","xyz"]}"""));

        Assert.Equal(ProfileErrorCode.InvalidValue, error.Code);
        Assert.Equal("modules.drivers.sources[1]", error.Path);
    }

    [Fact]
    public void Empty_sources_is_error()
    {
        var error = Assert.Single(Validate("""{"enabled":true,"sources":[]}"""));

        Assert.Equal(ProfileErrorCode.InvalidValue, error.Code);
        Assert.Equal("modules.drivers.sources", error.Path);
    }

    [Fact]
    public void Duplicate_source_is_error()
    {
        var error = Assert.Single(Validate("""{"enabled":true,"sources":["gpu","gpu"]}"""));

        Assert.Equal(ProfileErrorCode.InvalidValue, error.Code);
        Assert.Equal("modules.drivers.sources[1]", error.Path);
    }

    [Fact]
    public void Unknown_property_is_error()
    {
        var error = Assert.Single(Validate("""{"enabled":true,"sources":["gpu"],"bogus":1}"""));

        Assert.Equal(ProfileErrorCode.InvalidValue, error.Code);
        Assert.Equal("modules.drivers.bogus", error.Path);
    }

    [Fact]
    public void Wrong_typed_fields_are_errors()
    {
        var errors = Validate("""{"enabled":"yes","sources":"gpu","networkDriverBackup":5}""");

        Assert.Equal(
            ["modules.drivers.enabled", "modules.drivers.sources", "modules.drivers.networkDriverBackup"],
            errors.Select(e => e.Path));
    }

    [Fact]
    public void IsEnabled_reads_the_enabled_flag()
    {
        Assert.True(Module.IsEnabled(Json("""{"enabled":true,"sources":["gpu"]}""")));
        Assert.False(Module.IsEnabled(Json("""{"enabled":false,"sources":["gpu"]}""")));
        Assert.False(Module.IsEnabled(Json("""{"sources":["gpu"]}""")));
    }

    [Fact]
    public void Steps_follow_fixed_order()
    {
        var steps = Module.CreateSteps(Json("""{"enabled":true,"sources":["gpu","windowsUpdate"]}"""));

        Assert.Equal(
            ["drivers.network-check", "drivers.windows-update", "drivers.gpu", "drivers.leftover-report"],
            steps.Select(s => s.Id));
    }

    [Fact]
    public void Network_and_leftover_steps_are_always_present()
    {
        var steps = Module.CreateSteps(Json("""{"enabled":true,"sources":["oem"]}"""));

        Assert.Equal("drivers.network-check", steps[0].Id);
        Assert.Equal("drivers.leftover-report", steps[^1].Id);
        Assert.Equal(3, steps.Count);
    }

    [Theory]
    [InlineData("net", @"E:\usb", @"E:\usb\net")]
    [InlineData(@"D:\\net", @"E:\usb", @"D:\net")]
    public async Task Network_check_step_uses_the_backup_folder_resolved_against_the_profile_folder(
        string backup, string baseDirectory, string expected)
    {
        var json = Json($$"""{"enabled":true,"sources":["gpu"],"networkDriverBackup":"{{backup}}"}""");
        var procs = new FakeProcessRunner();
        procs.Enqueue("pnputil.exe", new ProcessResult(0, "", "", TimeSpan.Zero, false));
        var files = new InMemoryFileSystem();
        files.CreateDirectory(expected);
        var ctx = TestServices.Context(TestServices.Create(processes: procs, network: new FakeNetworkProbe(true), files: files));

        var step = Module.CreateSteps(json, baseDirectory).First(s => s.Id == "drivers.network-check");
        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Contains(Path.Combine(expected, "*.inf"), Assert.Single(procs.Calls).Arguments);
    }
}
