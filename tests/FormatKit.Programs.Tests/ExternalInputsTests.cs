using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Core.Tests.Fakes;
using static FormatKit.Programs.Tests.ProgramsTestData;

namespace FormatKit.Programs.Tests;

public class ExternalInputsTests
{
    private static ProgramsModule Module(InMemoryFileSystem files) =>
        new(TestCatalog(), new AppPaths(@"C:\FormatKitTest"), files);

    private static InMemoryFileSystem WithImport(string path)
    {
        var files = new InMemoryFileSystem();
        files.Seed(path, """{ "Sources": [] }""");
        return files;
    }

    [Fact]
    public void Import_file_and_direct_paths_are_listed_with_their_json_paths()
    {
        var json = Json($$"""
            { "enabled": true, "acceptAgreements": true, "importFile": "old.json",
              "direct": [ { "name": "A", "url": "https://example.com/a.exe", "sha256": "{{Sha}}" },
                          { "name": "B", "path": "D:\\setups\\tool.msi" } ] }
            """);

        var inputs = Module(WithImport(@"E:\usb\old.json")).ExternalInputs(json, @"E:\usb");

        Assert.Equal(
            [
                new ExternalInput("modules.programs.importFile", @"E:\usb\old.json", ExternalInputKind.File),
                new ExternalInput("modules.programs.direct[1].path", @"D:\setups\tool.msi", ExternalInputKind.File),
            ],
            inputs);
    }

    [Fact]
    public void Absolute_import_file_is_kept_as_written()
    {
        var json = Json("""{ "enabled": true, "acceptAgreements": true, "importFile": "F:\\x\\old.json" }""");

        var input = Assert.Single(Module(WithImport(@"F:\x\old.json")).ExternalInputs(json, @"E:\usb"));

        Assert.Equal(@"F:\x\old.json", input.ResolvedPath);
    }

    [Fact]
    public void Section_without_paths_has_no_inputs()
    {
        Assert.Empty(Module(new InMemoryFileSystem()).ExternalInputs(Json("""{ "enabled": true }"""), @"E:\usb"));
    }
}
