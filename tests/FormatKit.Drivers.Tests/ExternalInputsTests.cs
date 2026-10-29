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
    public void Relative_backup_resolves_against_the_current_directory_not_the_profile()
    {
        var input = Assert.Single(Module.ExternalInputs(Json("""{"enabled":true,"sources":["gpu"],"networkDriverBackup":"net"}"""), @"E:\usb"));

        Assert.Equal(Path.GetFullPath("net"), input.ResolvedPath);
    }

    [Fact]
    public void Absent_backup_has_no_inputs()
    {
        Assert.Empty(Module.ExternalInputs(Json("""{"enabled":true,"sources":["gpu"]}"""), @"E:\usb"));
    }
}
