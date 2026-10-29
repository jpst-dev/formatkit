using static FormatKit.Programs.Tests.ProgramsTestData;

namespace FormatKit.Programs.Tests;

public class WingetImportFileTests
{
    [Fact]
    public void Reads_winget_export_sample()
    {
        var warnings = new List<string>();
        var entries = WingetImportFile.Read(ExportSample, warnings.Add);

        Assert.Equal(["Git.Git", "9NKSQGP7F2NH"], entries.Select(e => e.Id));
        Assert.Equal(["winget", "msstore"], entries.Select(e => e.Source));
        Assert.All(entries, e => Assert.Null(e.Version));
        Assert.Empty(warnings);
    }

    [Fact]
    public void Tolerates_extra_and_missing_optional_properties()
    {
        var json = """
            { "Extra": 1, "Sources": [
              { "Packages": [ { "PackageIdentifier": "A.B", "Version": "1.0", "Other": {} }, { "NoId": true } ],
                "SourceDetails": { "Name": "winget" } },
              { "SourceDetails": { "Name": "msstore" } },
              { "Packages": [ { "PackageIdentifier": "C.D" } ] }
            ] }
            """;
        var warnings = new List<string>();
        var entries = WingetImportFile.Read(json, warnings.Add);

        Assert.Equal(["A.B"], entries.Select(e => e.Id));
        Assert.Single(warnings); // the source without a name is skipped
    }

    [Fact]
    public void Skips_unknown_source_and_unsafe_ids_with_warning()
    {
        var json = """
            { "Sources": [
              { "Packages": [ { "PackageIdentifier": "Good.Id" }, { "PackageIdentifier": "bad id; calc" }, { "PackageIdentifier": "-rf" } ],
                "SourceDetails": { "Name": "winget" } },
              { "Packages": [ { "PackageIdentifier": "Other.App" } ], "SourceDetails": { "Name": "custom" } }
            ] }
            """;
        var warnings = new List<string>();
        var entries = WingetImportFile.Read(json, warnings.Add);

        Assert.Equal(["Good.Id"], entries.Select(e => e.Id));
        Assert.Equal(3, warnings.Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{ "Sources": 3 }""")]
    public void Invalid_json_throws(string json) =>
        Assert.Throws<InvalidDataException>(() => WingetImportFile.Read(json, _ => { }));
}
