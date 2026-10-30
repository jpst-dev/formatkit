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
        string action = DefaultAction,
        string extra = "")
    {
        var texts = $"\"en\": {en ?? Text()}";
        if (ptBr != string.Empty)
            texts += $", \"pt-BR\": {ptBr ?? Text("Nada conhecido.")}";
        return $$"""
            {
              "id": "{{id}}", "category": "{{category}}", "level": "{{level}}",
              "contested": {{contested.ToString().ToLowerInvariant()}}, "requiresReboot": false,
              "scope": "{{scope}}", "laptopSensitive": {{laptopSensitive.ToString().ToLowerInvariant()}},
              {{extra}}
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

    [Theory]
    [InlineData("startup")]
    [InlineData("interface")]
    [InlineData("ai")]
    [InlineData("appearance")]
    [InlineData("input")]
    [InlineData("sound")]
    [InlineData("power")]
    public void New_categories_accept_their_id_prefix(string category) =>
        Assert.Empty(Validate(Item(id: category + ".x", category: category)));

    [Fact]
    public void OptIn_RestartsExplorer_And_Condition_Parse()
    {
        var catalog = CleanupCatalog.Parse($$"""{ "schemaVersion": 1, "items": [ {{Item(extra: "\"optIn\": true, \"restartsExplorer\": true, \"condition\": \"noPrinter\",")}} ] }""");

        var item = Assert.Single(catalog.Items);
        Assert.True(item.OptIn);
        Assert.True(item.IsOptIn);
        Assert.True(item.RestartsExplorer);
        Assert.Equal(ItemCondition.NoPrinter, item.Condition);
        Assert.Empty(CatalogValidator.Validate(catalog));
    }

    [Fact]
    public void Item_without_the_new_fields_is_not_opt_in()
    {
        var item = Assert.Single(CleanupCatalog.Parse($$"""{ "schemaVersion": 1, "items": [ {{Item()}} ] }""").Items);

        Assert.False(item.IsOptIn);
        Assert.False(item.RestartsExplorer);
        Assert.Null(item.Condition);
    }

    [Fact]
    public void Unknown_condition_fails_parsing() =>
        Assert.Throws<System.Text.Json.JsonException>(() => CleanupCatalog.Parse(
            $$"""{ "schemaVersion": 1, "items": [ {{Item(extra: "\"condition\": \"noScanner\",")}} ] }"""));

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
    public void Blank_ifFileExists_is_rejected() =>
        AssertError(
            Validate(Item(action: """{ "type": "registry", "path": "HKCU\\Software\\Test", "name": "N", "kind": "dword", "value": 1, "ifFileExists": " " }""")),
            "ifFileExists");

    [Fact]
    public void IfFileExists_is_accepted() =>
        Assert.Empty(Validate(Item(id: "developer.test", category: "developer", scope: "machine",
            action: """{ "type": "registry", "path": "HKLM\\Software\\Test", "name": "N", "kind": "dword", "value": 1, "ifFileExists": "%SystemRoot%\\System32\\sudo.exe" }""")));

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
    public void Integer_enum_values_are_rejected() =>
        Assert.Throws<System.Text.Json.JsonException>(() => CleanupCatalog.Parse(
            $$"""{ "schemaVersion": 1, "items": [ {{Item().Replace("\"level\": \"safe\"", "\"level\": 2")}} ] }"""));

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

    private static string BinaryAction(string value) =>
        $$"""{ "type": "registry", "path": "HKCU\\Software\\Test", "name": "B", "kind": "binary", "value": {{value}} }""";

    [Fact]
    public void Valid_binary_value_is_accepted() =>
        Assert.Empty(Validate(Item(action: BinaryAction("\"0300000000000000\""))));

    [Theory]
    [InlineData("\"030\"")]
    [InlineData("\"zz\"")]
    [InlineData("\"03 00\"")]
    [InlineData("3")]
    public void Odd_length_or_non_hex_binary_value_is_rejected(string value) =>
        AssertError(Validate(Item(action: BinaryAction(value))), "does not match kind");

    private static string TaskAction(string path, string name) =>
        $$"""{ "type": "scheduledTask", "path": "{{path.Replace("\\", "\\\\")}}", "name": "{{name}}", "enabled": false }""";

    [Theory]
    [InlineData(@"\Microsoft\Windows\WindowsUpdate\", "Scheduled Start")]
    [InlineData(@"\Microsoft\Windows\WindowsUpdate\Sub\", "Anything")]
    [InlineData(@"\Microsoft\Windows\UpdateOrchestrator\", "Reboot")]
    [InlineData(@"\Microsoft\Windows\WaaSMedic\", "PerformRemediation")]
    [InlineData(@"\Microsoft\Windows\Defrag\", "ScheduledDefrag")]
    [InlineData(@"\Microsoft\Windows\PI\", "Secure-Boot-Update")]
    [InlineData(@"\Microsoft\Windows\Autochk\", "Proxy")]
    [InlineData(@"\Microsoft\XblGameSave\", "XblGameSaveTask")]
    [InlineData(@"\Microsoft\Windows\Windows Defender\", "Windows Defender Scheduled Scan")]
    [InlineData(@"\microsoft\windows\windows defender\", "x")]
    [InlineData(@"\Microsoft\Windows\Windows Error Reporting\", "QueueReporting")]
    public void Protected_scheduled_task_is_rejected(string path, string name) =>
        AssertError(Validate(Item(scope: "machine", action: TaskAction(path, name))), "protected");

    [Fact]
    public void Unprotected_scheduled_task_is_accepted() =>
        Assert.Empty(Validate(Item(scope: "machine", action: TaskAction(@"\Microsoft\Windows\Application Experience\", "ProgramDataUpdater"))));

    [Theory]
    [InlineData(@"Microsoft\Windows\Application Experience\")]
    [InlineData(@"\Microsoft\Windows\Application Experience")]
    [InlineData("")]
    public void Scheduled_task_path_without_leading_and_trailing_backslash_is_rejected(string path) =>
        AssertError(Validate(Item(scope: "machine", action: TaskAction(path, "ProgramDataUpdater"))), "must start and end with a backslash");

    [Fact]
    public void Protected_task_helper_matches_exact_paths_and_prefixes_case_insensitively()
    {
        Assert.True(ProtectedLists.IsProtectedTask(@"\Microsoft\Windows\Defrag\", "scheduleddefrag"));
        Assert.False(ProtectedLists.IsProtectedTask(@"\Microsoft\Windows\Defrag\", "Other"));
        Assert.False(ProtectedLists.IsProtectedTask(@"\Microsoft\Windows\WindowsUpdateX\", "T"));
    }

    [Theory]
    [InlineData(@"\Microsoft\Windows\Windows*\", "Task")]
    [InlineData(@"\Microsoft\Windows\Windows?efender\", "Task")]
    [InlineData(@"\Microsoft\Windows\[W]indowsUpdate\", "Task")]
    [InlineData(@"\Microsoft\Windows\Application Experience\", "*")]
    [InlineData(@"\Microsoft\Windows\Application Experience\", "Program?ataUpdater")]
    [InlineData(@"\Microsoft\Windows\Application Experience\", "[P]rogramDataUpdater")]
    public void Scheduled_task_wildcards_are_rejected(string path, string name) =>
        AssertError(Validate(Item(scope: "machine", action: TaskAction(path, name))), "wildcard");
}
