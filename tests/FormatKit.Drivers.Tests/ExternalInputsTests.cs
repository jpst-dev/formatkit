using System.Text.Json;
using FormatKit.Core.Profiles;
using FormatKit.Drivers.Catalog;

namespace FormatKit.Drivers.Tests;

public class ExternalInputsTests
{
    private static readonly DriversModule Module = new(VendorCatalog.LoadEmbedded(), TimeSpan.Zero);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Network_driver_backup_is_a_folder_used_as_written()
    {
        var inputs = Module.ExternalInputs(Json("""{"enabled":true,"sources":["gpu"],"networkDriverBackup":"E:\\net"}"""), @"E:\usb");

        Assert.Equal([new ExternalInput("modules.drivers.networkDriverBackup", @"E:\net", ExternalInputKind.Folder)], inputs);
    }

    [Fact]
    public void Relative_backup_resolves_against_the_profile_folder()
    {
        var input = Assert.Single(Module.ExternalInputs(Json("""{"enabled":true,"sources":["gpu"],"networkDriverBackup":"net"}"""), @"E:\usb"));

        Assert.Equal(@"E:\usb\net", input.ResolvedPath);
    }

    [Fact]
    public void Absolute_backup_ignores_the_profile_folder()
    {
        var input = Assert.Single(Module.ExternalInputs(Json("""{"enabled":true,"sources":["gpu"],"networkDriverBackup":"D:\\net"}"""), @"E:\usb"));

        Assert.Equal(@"D:\net", input.ResolvedPath);
    }

    [Fact]
    public void Relative_backup_without_a_profile_folder_resolves_against_the_current_directory()
    {
        var input = Assert.Single(Module.ExternalInputs(Json("""{"enabled":true,"sources":["gpu"],"networkDriverBackup":"net"}"""), null));

        Assert.Equal(Path.GetFullPath("net"), input.ResolvedPath);
    }

    [Fact]
    public void Absent_backup_has_no_inputs()
    {
        Assert.Empty(Module.ExternalInputs(Json("""{"enabled":true,"sources":["gpu"]}"""), @"E:\usb"));
    }
}
