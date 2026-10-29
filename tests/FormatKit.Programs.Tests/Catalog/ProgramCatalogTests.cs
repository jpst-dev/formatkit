using FormatKit.Programs.Catalog;

namespace FormatKit.Programs.Tests.Catalog;

public class ProgramCatalogTests
{
    private static string Entry(
        string id = "browsers.firefox",
        string category = "browsers",
        string wingetId = "Mozilla.Firefox",
        string source = "winget",
        string? context = null,
        string? ptBr = null,
        string en = """{ "name": "Firefox", "description": "Browser" }""")
    {
        var contextJson = context is null ? "" : $", \"context\": \"{context}\"";
        var pt = ptBr ?? """{ "name": "Firefox", "description": "Navegador" }""";
        return $$"""
            { "id": "{{id}}", "category": "{{category}}", "wingetId": "{{wingetId}}", "source": "{{source}}"{{contextJson}},
              "text": { "en": {{en}}, "pt-BR": {{pt}} } }
            """;
    }

    private static string Catalog(params string[] entries) =>
        $$"""{ "schemaVersion": 1, "programs": [ {{string.Join(",", entries)}} ] }""";

    private static void AssertRejected(string json, string fragment)
    {
        var ex = Assert.Throws<InvalidDataException>(() => ProgramCatalog.Parse(json));
        Assert.Contains(fragment, ex.Message);
    }

    [Fact]
    public void Embedded_catalog_loads()
    {
        var catalog = ProgramCatalog.LoadEmbedded();

        Assert.Equal(1, catalog.SchemaVersion);
        Assert.Empty(ProgramCatalogValidator.Validate(catalog));
    }

    [Fact]
    public void Parse_reads_all_fields_and_defaults_context_to_machine()
    {
        var catalog = ProgramCatalog.Parse(Catalog(
            Entry(),
            Entry("utilities.powertoys", "utilities", "Microsoft.PowerToys", "msstore", "user")));

        var first = catalog.Programs[0];
        Assert.Equal("browsers.firefox", first.Id);
        Assert.Equal(ProgramCategory.Browsers, first.Category);
        Assert.Equal("Mozilla.Firefox", first.WingetId);
        Assert.Equal("winget", first.Source);
        Assert.Equal(InstallContext.Machine, first.Context);
        Assert.Equal("Navegador", first.Text["pt-BR"].Description);
        Assert.Equal("msstore", catalog.Programs[1].Source);
        Assert.Equal(InstallContext.User, catalog.Programs[1].Context);
    }

    [Fact]
    public void Integer_enum_values_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            ProgramCatalog.Parse(Catalog(Entry()).Replace("\"category\": \"browsers\"", "\"category\": 0")));
    }

    [Fact]
    public void Rejects_id_with_bad_shape() =>
        AssertRejected(Catalog(Entry(id: "Firefox")), "id must match");

    [Fact]
    public void Rejects_id_prefix_that_differs_from_category() =>
        AssertRejected(Catalog(Entry(id: "media.firefox")), "prefix");

    [Fact]
    public void Rejects_duplicate_ids() =>
        AssertRejected(Catalog(Entry(), Entry(wingetId: "Other.Package")), "duplicate id");

    [Fact]
    public void Rejects_unsafe_winget_id() =>
        AssertRejected(Catalog(Entry(wingetId: "Mozilla Firefox; calc")), "Unsafe wingetId");

    [Fact]
    public void Rejects_unknown_source() =>
        AssertRejected(Catalog(Entry(source: "chocolatey")), "source must be");

    [Fact]
    public void Rejects_missing_language() =>
        AssertRejected(Catalog(Entry().Replace("\"pt-BR\"", "\"fr\"")), "missing 'pt-BR'");

    [Fact]
    public void Rejects_blank_name_or_description() =>
        AssertRejected(Catalog(Entry(en: """{ "name": " ", "description": "Browser" }""")), "must not be blank");

    [Fact]
    public void Parse_reads_scope_and_defaults_it_to_null()
    {
        var catalog = ProgramCatalog.Parse(Catalog(
            Entry(),
            Entry("browsers.brave", wingetId: "Brave.Brave").Replace("\"source\": \"winget\"", "\"source\": \"winget\", \"scope\": \"machine\""),
            Entry("browsers.opera", wingetId: "Opera.Opera", context: "user").Replace("\"source\": \"winget\"", "\"source\": \"winget\", \"scope\": \"user\"")));

        Assert.Equal([null, "machine", "user"], catalog.Programs.Select(p => p.Scope));
    }

    [Theory]
    [InlineData("Machine")]
    [InlineData("system")]
    [InlineData("")]
    public void Rejects_unknown_scope(string scope) =>
        AssertRejected(Catalog(Entry().Replace("\"source\": \"winget\"", $"\"source\": \"winget\", \"scope\": \"{scope}\"")), "scope must be");

    [Fact]
    public void Rejects_machine_scope_on_user_context() =>
        AssertRejected(
            Catalog(Entry(context: "user").Replace("\"source\": \"winget\"", "\"source\": \"winget\", \"scope\": \"machine\"")),
            "scope 'machine' on a user-context");

    [Fact]
    public void Rejects_same_winget_id_and_source_twice() =>
        AssertRejected(Catalog(Entry(), Entry(id: "browsers.firefox-again")), "duplicate winget package");
}
