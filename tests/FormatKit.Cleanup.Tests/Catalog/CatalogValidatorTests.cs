using System.Globalization;
using FormatKit.Cleanup.Catalog;

namespace FormatKit.Cleanup.Tests.Catalog;

[Collection("Culture")]
public class CatalogValidatorTests : IDisposable
{
    private const string DefaultAction =
        """{ "type": "registry", "path": "HKCU\\Software\\Test", "name": "Enabled", "kind": "dword", "value": 0 }""";

    private readonly CultureInfo _original = CultureInfo.CurrentUICulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _original;

    private static string Text(string breaks = "Nothing known.") =>
        $$"""{ "title": "T", "what": "W", "breaks": "{{breaks}}", "gain": "G" }""";

    private static string Item(
        string id = "privacy.test",
        string category = "privacy",
        string level = "safe",
        bool contested = false,
        string scope = "user",
        bool laptopSensitive = false,
        string? en = null,
        string? ptBr = null,
        string action = DefaultAction)
    {
        var texts = $"\"en\": {en ?? Text()}";
        if (ptBr != string.Empty)
            texts += $", \"pt-BR\": {ptBr ?? Text("Nada conhecido.")}";
        return $$"""
            {
              "id": "{{id}}", "category": "{{category}}", "level": "{{level}}",
              "contested": {{contested.ToString().ToLowerInvariant()}}, "requiresReboot": false,
              "scope": "{{scope}}", "laptopSensitive": {{laptopSensitive.ToString().ToLowerInvariant()}},
              "text": { {{texts}} },
              "actions": [ {{action}} ]
            }
            """;
    }

    private static IReadOnlyList<string> Validate(params string[] items) =>
        CatalogValidator.Validate(CleanupCatalog.Parse(
            $$"""{ "schemaVersion": 1, "items": [ {{string.Join(",", items)}} ] }"""));

    private static void AssertError(IReadOnlyList<string> errors, string fragment) =>
        Assert.Contains(errors, e => e.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Valid_minimal_catalog_has_no_errors() =>
        Assert.Empty(Validate(Item()));

    [Fact]
    public void Duplicate_id_is_rejected() =>
        AssertError(Validate(Item(), Item()), "duplicate id");

    [Fact]
    public void Id_prefix_must_match_category() =>
        AssertError(Validate(Item(id: "apps.test")), "prefix");

    [Fact]
    public void Missing_portuguese_text_is_rejected() =>
        AssertError(Validate(Item(ptBr: string.Empty)), "pt-BR");

    [Fact]
    public void Blank_breaks_is_rejected() =>
        AssertError(Validate(Item(en: Text(breaks: " "))), "'breaks' is blank");

    [Fact]
    public void Contested_below_aggressive_is_rejected() =>
        AssertError(Validate(Item(contested: true, level: "balanced")), "contested");

    [Fact]
    public void Protected_service_is_rejected() =>
        AssertError(
            Validate(Item(scope: "machine", action: """{ "type": "service", "name": "W32Time", "startType": "disabled" }""")),
            "protected");

    [Fact]
    public void Appx_outside_allowlist_is_rejected() =>
        AssertError(
            Validate(Item(id: "apps.test", category: "apps", scope: "machine",
                action: """{ "type": "appx", "package": "Microsoft.WindowsStore", "storeId": "9WZDNCRFJBMP" }""")),
            "allowlist");

    [Fact]
    public void Dword_value_out_of_range_is_rejected() =>
        AssertError(
            Validate(Item(action: """{ "type": "registry", "path": "HKCU\\Software\\Test", "name": "N", "kind": "dword", "value": 4294967296 }""")),
            "does not match kind");

    [Fact]
    public void Dword_max_is_accepted() =>
        Assert.Empty(Validate(Item(action: """{ "type": "registry", "path": "HKCU\\Software\\Test", "name": "N", "kind": "dword", "value": 4294967295 }""")));

    [Fact]
    public void User_scope_with_HKLM_is_rejected() =>
        AssertError(
            Validate(Item(action: """{ "type": "registry", "path": "HKLM\\Software\\Test", "name": "N", "kind": "dword", "value": 0 }""")),
            "scope user");

    [Fact]
    public void Unknown_named_handler_is_rejected() =>
        AssertError(
            Validate(Item(scope: "machine", action: """{ "type": "named", "handler": "nope" }""")),
            "unknown named handler");

    [Fact]
    public void Laptop_sensitive_without_power_action_is_rejected() =>
        AssertError(Validate(Item(laptopSensitive: true)), "laptopSensitive");

    [Fact]
    public void Optional_feature_absent_state_is_rejected() =>
        AssertError(
            Validate(Item(scope: "machine", action: """{ "type": "optionalFeature", "name": "SMB1Protocol", "state": "absent" }""")),
            "state must be enabled or disabled");

    [Fact]
    public void Removable_apps_never_overlap_never_remove_list() =>
        Assert.All(ProtectedLists.RemovableApps, package => Assert.False(ProtectedLists.IsNeverRemove(package), package));

    [Fact]
    public void Embedded_catalog_is_valid() =>
        Assert.NotNull(CleanupCatalog.LoadEmbedded());

    [Fact]
    public void LocalizedText_picks_portuguese_for_pt_BR()
    {
        var item = CleanupCatalog.Parse($$"""{ "schemaVersion": 1, "items": [ {{Item()}} ] }""").Items.Single();

        CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");
        Assert.Equal("Nada conhecido.", item.LocalizedText().Breaks);

        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        Assert.Equal("Nothing known.", item.LocalizedText().Breaks);
    }
}
