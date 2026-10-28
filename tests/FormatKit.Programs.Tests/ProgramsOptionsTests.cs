using FormatKit.Core.Profiles;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Programs.Catalog;
using static FormatKit.Programs.Tests.ProgramsTestData;

namespace FormatKit.Programs.Tests;

public class ProgramsOptionsTests
{
    private const string Path = "$.modules.programs";

    private static (ProgramsOptions? Options, IReadOnlyList<ProfileError> Errors) Parse(string json, InMemoryFileSystem? files = null) =>
        ProgramsOptions.Parse(Json(json), Path, TestCatalog(), files ?? new InMemoryFileSystem());

    private static void AssertError(string json, string errorPath, ProfileErrorCode? code = null, InMemoryFileSystem? files = null)
    {
        var (options, errors) = Parse(json, files);
        Assert.Null(options);
        var error = Assert.Single(errors, e => e.Path == errorPath);
        if (code is not null)
            Assert.Equal(code, error.Code);
    }

    private static string Direct(string body) =>
        $$"""{ "acceptAgreements": true, "direct": [ {{body}} ] }""";

    [Fact]
    public void Valid_full_profile_parses()
    {
        var files = new InMemoryFileSystem();
        files.Seed(@"C:\backup\old-pc-programs.json", ExportSample);
        var json = $$"""
            {
              "enabled": true, "acceptAgreements": true, "upgrade": false,
              "catalog": ["browsers.firefox", "communication.discord", "games.steam"],
              "winget": [
                { "id": "Notepad++.Notepad++" },
                { "id": "Python.Python.3.12", "version": "3.12.7" },
                { "id": "9NBLGGH4NNS1", "source": "msstore" },
                { "id": "Some.UserApp", "context": "user" }
              ],
              "importFile": "C:\\backup\\old-pc-programs.json",
              "direct": [
                { "name": "My app", "url": "https://example.com/setup.exe", "sha256": "{{Sha}}", "args": ["/S"] },
                { "name": "Local tool", "path": "D:\\setups\\tool.msi" }
              ]
            }
            """;

        var (options, errors) = Parse(json, files);

        Assert.Empty(errors);
        Assert.NotNull(options);
        Assert.True(options.AcceptAgreements);
        Assert.False(options.Upgrade);
        Assert.Equal(["browsers.firefox", "communication.discord", "games.steam"], options.Catalog);
        Assert.Equal(4, options.Winget.Count);
        Assert.Equal(new WingetEntry("Notepad++.Notepad++", "winget", null, InstallContext.Machine), options.Winget[0]);
        Assert.Equal("3.12.7", options.Winget[1].Version);
        Assert.Equal("msstore", options.Winget[2].Source);
        Assert.Equal(InstallContext.User, options.Winget[3].Context);
        Assert.Equal(@"C:\backup\old-pc-programs.json", options.ImportFile);
        Assert.Equal(["Git.Git", "9NKSQGP7F2NH"], options.Imported!.Select(e => e.Id));
        Assert.Equal(2, options.Direct.Count);
        Assert.Equal(["/S"], options.Direct[0].Args);
        Assert.Equal(@"D:\setups\tool.msi", options.Direct[1].Path);
        Assert.Null(options.Direct[1].Sha256);
    }

    [Fact]
    public void Items_without_accept_agreements_is_error() =>
        AssertError("""{ "catalog": ["games.steam"] }""", $"{Path}.acceptAgreements");

    [Fact]
    public void Items_from_import_file_need_accept_agreements()
    {
        var files = new InMemoryFileSystem();
        files.Seed(@"C:\x.json", ExportSample);
        AssertError("""{ "importFile": "C:\\x.json" }""", $"{Path}.acceptAgreements", files: files);
    }

    [Fact]
    public void Empty_section_is_valid_without_accept_agreements()
    {
        var (options, errors) = Parse("""{ "enabled": true }""");

        Assert.Empty(errors);
        Assert.NotNull(options);
        Assert.False(options.AcceptAgreements);
        Assert.Empty(options.Catalog);
        Assert.Empty(options.Winget);
        Assert.Empty(options.Direct);
        Assert.Null(options.ImportFile);
    }

    [Fact]
    public void Unknown_catalog_id_is_error() =>
        AssertError("""{ "acceptAgreements": true, "catalog": ["games.steam", "nope.nothing"] }""", $"{Path}.catalog[1]");

    [Fact]
    public void Unsafe_winget_id_is_error()
    {
        AssertError("""{ "acceptAgreements": true, "winget": [ { "id": "a b; calc" } ] }""", $"{Path}.winget[0].id");
        AssertError("""{ "acceptAgreements": true, "winget": [ { "id": "A.B", "version": "1 --force" } ] }""", $"{Path}.winget[0].version");
    }

    [Fact]
    public void Bad_source_or_context_is_error()
    {
        AssertError("""{ "acceptAgreements": true, "winget": [ { "id": "A.B", "source": "evil" } ] }""", $"{Path}.winget[0].source");
        AssertError("""{ "acceptAgreements": true, "winget": [ { "id": "A.B", "context": "root" } ] }""", $"{Path}.winget[0].context");
    }

    [Fact]
    public void Http_url_is_error() =>
        AssertError(Direct($$"""{ "name": "x", "url": "http://example.com/s.exe", "sha256": "{{Sha}}" }"""), $"{Path}.direct[0].url");

    [Fact]
    public void Url_without_sha256_is_error() =>
        AssertError(Direct("""{ "name": "x", "url": "https://example.com/s.exe" }"""), $"{Path}.direct[0].sha256", ProfileErrorCode.MissingField);

    [Theory]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Short_or_non_hex_sha256_is_error(string sha) =>
        AssertError(Direct($$"""{ "name": "x", "path": "D:\\s.exe", "sha256": "{{sha}}" }"""), $"{Path}.direct[0].sha256");

    [Fact]
    public void Both_or_neither_url_and_path_is_error()
    {
        AssertError(Direct($$"""{ "name": "x", "url": "https://e.com/s.exe", "path": "D:\\s.exe", "sha256": "{{Sha}}" }"""), $"{Path}.direct[0]");
        AssertError(Direct("""{ "name": "x" }"""), $"{Path}.direct[0]");
    }

    [Fact]
    public void Unsupported_extension_is_error()
    {
        AssertError(Direct("""{ "name": "x", "path": "D:\\s.zip" }"""), $"{Path}.direct[0].path");
        AssertError(Direct($$"""{ "name": "x", "url": "https://e.com/s.bat", "sha256": "{{Sha}}" }"""), $"{Path}.direct[0].url");
    }

    [Fact]
    public void Args_on_msi_is_error() =>
        AssertError(Direct("""{ "name": "x", "path": "D:\\s.msi", "args": ["/qn"] }"""), $"{Path}.direct[0].args");

    [Fact]
    public void Missing_import_file_is_error() =>
        AssertError("""{ "importFile": "C:\\nope.json" }""", $"{Path}.importFile", ProfileErrorCode.FileNotFound);

    [Fact]
    public void Malformed_import_file_is_error()
    {
        var files = new InMemoryFileSystem();
        files.Seed(@"C:\bad.json", "not json");
        AssertError("""{ "importFile": "C:\\bad.json" }""", $"{Path}.importFile", ProfileErrorCode.InvalidValue, files);
    }
}
