using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Diagnosis;

namespace FormatKit.Cleanup.Tests.Catalog;

/// <summary>Checks the shipped cleanup.json and known-issues.json against the spec (sections 7 and 11).</summary>
public class EmbeddedContentTests
{
    /// <summary>The item ids of spec section 7, minus <see cref="Dropped"/>.</summary>
    private static readonly string[] SpecItemIds =
    [
        "apps.clipchamp", "apps.solitaire", "apps.news", "apps.weather", "apps.get-help", "apps.tips",
        "apps.feedback-hub", "apps.power-automate",
        "apps.teams", "apps.xbox", "apps.phone-link", "apps.todo",
        "privacy.advertising-id", "privacy.tailored-experiences", "privacy.start-suggestions",
        "privacy.lock-screen-tips", "privacy.settings-suggestions", "privacy.tips-notifications",
        "privacy.silent-app-installs", "privacy.activity-history", "privacy.inking-typing",
        "privacy.feedback-frequency",
        "privacy.diagnostic-data", "privacy.start-web-search", "privacy.online-speech",
        "performance.game-mode", "performance.gpu-scheduling", "performance.game-bar-recording",
        "performance.high-performance-plan", "performance.pointer-precision",
        "performance.ultimate-plan", "performance.memory-integrity-off",
        "performance.system-responsiveness", "performance.network-throttling",
        "performance.games-task-priority", "performance.priority-separation",
        "performance.power-throttling-off",
        "developer.developer-mode", "developer.long-paths", "developer.show-extensions",
        "developer.show-hidden", "developer.sudo", "developer.ps-execution-policy",
        // Windows settings design (2026-10-29-windows-settings-design.md), section 7.
        "startup.onedrive", "startup.edge-startup-boost", "startup.edge-background",
        "apps.widgets", "apps.outlook-new", "apps.dev-home", "apps.bing-search", "apps.copilot", "apps.office-hub",
        "interface.taskbar-left", "interface.taskbar-search-icon", "interface.taskbar-task-view-off",
        "interface.taskbar-end-task", "interface.explorer-this-pc", "interface.classic-context-menu",
        "interface.start-recommendations-off", "interface.start-web-results-off",
        "ai.recall-off", "ai.click-to-do-off", "ai.paint-off", "ai.notepad-off", "ai.edge-copilot-off",
        "ai.fabric-service-manual",
        "privacy.welcome-experience", "privacy.delivery-optimization-no-peers", "privacy.diagtrack-manual",
        "privacy.telemetry-tasks", "privacy.compatibility-appraiser",
        "performance.formatkit-plan", "performance.spooler-off", "performance.trkwks-manual",
        "appearance.dark-theme", "appearance.transparency-off", "appearance.animations-off",
        "input.ptbr-us-international", "input.sticky-keys-shortcut-off",
        "sound.communications-no-ducking", "sound.no-sounds",
        "power.fast-startup-off", "power.hibernation-off", "power.monitor-off-15", "power.monitor-off-30",
        "power.sleep-never",
    ];

    /// <summary>
    /// Items left out because neither Microsoft documentation nor the test VM could confirm their location, the app
    /// cannot be reinstalled, or another item already does the same. See docs/cleanup-not-included.md.
    /// </summary>
    private static readonly string[] Dropped =
    [
        "apps.tips",
        "privacy.settings-suggestions",
        "performance.gpu-scheduling", "performance.network-throttling", "performance.game-bar-recording",
        "apps.dev-home",
        "interface.taskbar-search-icon", "interface.taskbar-end-task", "interface.explorer-this-pc",
        "interface.classic-context-menu", "interface.start-web-results-off",
        "ai.edge-copilot-off",
        "privacy.welcome-experience",
        "privacy.start-suggestions", "privacy.lock-screen-tips",
        "sound.communications-no-ducking",
    ];

    private sealed record Expected(
        string Id, CleanupLevel Level, bool Contested = false, bool OptIn = false, bool LaptopSensitive = false,
        bool RestartsExplorer = false, ItemCondition? Condition = null);

    /// <summary>The flags of every item of the Windows settings design, section 7, that survived verification.</summary>
    private static readonly Expected[] SettingsItems =
    [
        new("startup.onedrive", CleanupLevel.Safe),
        new("startup.edge-startup-boost", CleanupLevel.Safe),
        new("startup.edge-background", CleanupLevel.Safe),
        new("apps.widgets", CleanupLevel.Balanced),
        new("apps.outlook-new", CleanupLevel.Balanced),
        new("apps.bing-search", CleanupLevel.Balanced),
        new("apps.copilot", CleanupLevel.Balanced),
        new("apps.office-hub", CleanupLevel.Aggressive, Contested: true),
        new("apps.phone-link", CleanupLevel.Balanced),
        new("interface.taskbar-left", CleanupLevel.Safe, OptIn: true, RestartsExplorer: true),
        new("interface.taskbar-task-view-off", CleanupLevel.Safe, RestartsExplorer: true),
        new("interface.start-recommendations-off", CleanupLevel.Safe, RestartsExplorer: true),
        new("ai.recall-off", CleanupLevel.Safe),
        new("ai.click-to-do-off", CleanupLevel.Safe),
        new("ai.paint-off", CleanupLevel.Safe),
        new("ai.notepad-off", CleanupLevel.Safe),
        new("ai.fabric-service-manual", CleanupLevel.Aggressive, Contested: true),
        new("privacy.tips-notifications", CleanupLevel.Safe),
        new("privacy.silent-app-installs", CleanupLevel.Safe),
        new("privacy.delivery-optimization-no-peers", CleanupLevel.Safe),
        new("privacy.diagtrack-manual", CleanupLevel.Balanced),
        new("privacy.telemetry-tasks", CleanupLevel.Balanced),
        new("privacy.compatibility-appraiser", CleanupLevel.Aggressive, Contested: true),
        new("performance.formatkit-plan", CleanupLevel.Aggressive, Contested: true, OptIn: true, LaptopSensitive: true),
        new("performance.spooler-off", CleanupLevel.Balanced, OptIn: true, Condition: ItemCondition.NoPrinter),
        new("performance.trkwks-manual", CleanupLevel.Balanced),
        new("appearance.dark-theme", CleanupLevel.Safe, OptIn: true),
        new("appearance.transparency-off", CleanupLevel.Safe, OptIn: true),
        new("appearance.animations-off", CleanupLevel.Safe, OptIn: true),
        new("input.ptbr-us-international", CleanupLevel.Safe, OptIn: true),
        new("input.sticky-keys-shortcut-off", CleanupLevel.Safe),
        new("sound.no-sounds", CleanupLevel.Safe, OptIn: true),
        new("power.fast-startup-off", CleanupLevel.Safe),
        new("power.hibernation-off", CleanupLevel.Balanced, LaptopSensitive: true),
        new("power.monitor-off-15", CleanupLevel.Safe, OptIn: true),
        new("power.monitor-off-30", CleanupLevel.Safe, OptIn: true),
        new("power.sleep-never", CleanupLevel.Safe, OptIn: true, LaptopSensitive: true),
    ];

    /// <summary>The Windows settings items with scope user (spec 7); every other item of <see cref="SettingsItems"/> is machine.</summary>
    private static readonly string[] UserScopeSettingsItems =
    [
        "startup.onedrive",
        "interface.taskbar-left", "interface.taskbar-task-view-off", "interface.start-recommendations-off",
        "privacy.tips-notifications", "privacy.silent-app-installs",
        "appearance.dark-theme", "appearance.transparency-off", "appearance.animations-off",
        "input.ptbr-us-international", "input.sticky-keys-shortcut-off",
        "sound.no-sounds",
    ];

    /// <summary>The telemetry tasks of spec 7.5 (the Compatibility Appraiser has its own contested item).</summary>
    private static readonly string[] TelemetryTasks =
    [
        "MareBackup", "StartupAppTask", "PcaPatchDbTask", "Consolidator", "UsbCeip",
        "Microsoft-Windows-DiskDiagnosticDataCollector", "DmClient", "DmClientOnScenarioDownload",
        "UsageDataReporting", "BootstrapUsageDataReporting",
    ];

    /// <summary>The contested items of the cleanup spec 7.3 and the Windows settings spec 7 that survived verification.</summary>
    private static readonly string[] ContestedItemIds =
    [
        "performance.system-responsiveness", "performance.games-task-priority",
        "performance.priority-separation", "performance.power-throttling-off",
        "apps.office-hub", "ai.fabric-service-manual", "privacy.compatibility-appraiser", "performance.formatkit-plan",
    ];

    /// <summary>Protected services whose Windows default is already Disabled, so a rule would always fire.</summary>
    private static readonly string[] DisabledByDefaultServices = ["ssh-agent"];

    /// <summary>Registry values from docs/cleanup-not-included.md that cleanup check detects.</summary>
    private static readonly (string Path, string Name)[] NotIncludedRegistryTweaks =
    [
        (@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive"),
        (@"HKLM\SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize"),
        (@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\*", "TcpAckFrequency"),
        (@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate"),
        (@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware"),
        (@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR"),
        (@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground"),
        (@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents"),
    ];

    /// <summary>Services from docs/cleanup-not-included.md (spec 7.11) that cleanup check reports when disabled.</summary>
    private static readonly string[] NotIncludedServices =
    [
        "SysMain", "WSearch", "WpnService", "CDPSvc", "LanmanServer", "iphlpsvc", "DoSvc", "WerSvc",
        "bthserv", "BTAGService", "BthAvctpSvc", "XboxNetApiSvc", "WaaSMedicSvc",
    ];

    /// <summary>Hardware ids of the devices docs/cleanup-not-included.md names.</summary>
    private static readonly string[] NotIncludedDevices = [@"ROOT\VDRVROOT", @"ROOT\UMBUS", @"ROOT\NDISVIRTUALBUS", @"ACPI\PNP0103"];

    [Fact]
    public void Catalog_is_valid() =>
        Assert.Empty(CatalogValidator.Validate(CleanupCatalog.LoadEmbedded()));

    [Fact]
    public void Every_spec_item_id_exists_or_is_listed_as_dropped()
    {
        var ids = CleanupCatalog.LoadEmbedded().Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var expected = SpecItemIds.Except(Dropped).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(expected.Except(ids));
        Assert.Empty(ids.Except(expected));
        Assert.Empty(Dropped.Intersect(ids));
    }

    [Fact]
    public void Developer_items_have_both_languages_and_only_long_paths_needs_a_reboot()
    {
        var items = CleanupCatalog.LoadEmbedded().Items.Where(i => i.Category == CleanupCategory.Developer).ToList();

        Assert.Equal(6, items.Count);
        Assert.All(items, item =>
        {
            Assert.Equal(CleanupLevel.Safe, item.Level);
            Assert.False(item.Contested);
            Assert.Equal(item.Id == "developer.long-paths", item.RequiresReboot);
            Assert.Equal(item.Id is "developer.show-extensions" or "developer.show-hidden" ? ItemScope.User : ItemScope.Machine, item.Scope);
            Assert.False(string.IsNullOrWhiteSpace(item.Text["en"].Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Text["pt-BR"].Gain));
        });
        var sudo = Assert.IsType<RegistryAction>(Assert.Single(items.Single(i => i.Id == "developer.sudo").Actions));
        Assert.Equal(@"%SystemRoot%\System32\sudo.exe", sudo.IfFileExists);
    }

    [Fact]
    public void Every_contested_item_is_aggressive() =>
        Assert.All(
            CleanupCatalog.LoadEmbedded().Items.Where(i => i.Contested),
            item => Assert.Equal(CleanupLevel.Aggressive, item.Level));

    [Fact]
    public void Contested_items_are_exactly_the_expected_ones() =>
        Assert.Equal(
            ContestedItemIds.Order(StringComparer.Ordinal),
            CleanupCatalog.LoadEmbedded().Items.Where(i => i.Contested).Select(i => i.Id).Order(StringComparer.Ordinal));

    /// <summary>Contested items that change a service, task, app or power plan have no registry value for a rule to read.</summary>
    [Fact]
    public void Every_contested_registry_item_has_a_registry_value_rule()
    {
        var rules = KnownIssues.LoadEmbedded().Issues.Select(i => i.Rule).OfType<RegistryValueRule>().ToList();

        Assert.All(CleanupCatalog.LoadEmbedded().Items.Where(i => i.Contested && i.Actions.Any(a => a is RegistryAction)), item => Assert.Contains(rules, rule =>
            item.Actions.OfType<RegistryAction>().Any(action =>
                string.Equals(action.Path, rule.Path, StringComparison.OrdinalIgnoreCase)
                && string.Equals(action.Name, rule.Name, StringComparison.OrdinalIgnoreCase))));
    }

    [Fact]
    public void Windows_settings_items_have_the_spec_flags_and_scope()
    {
        var catalog = CleanupCatalog.LoadEmbedded();

        Assert.All(SettingsItems, expected =>
        {
            var item = catalog.Find(expected.Id);
            Assert.True(item is not null, expected.Id + " is missing");
            Assert.Equal(expected, new Expected(item.Id, item.Level, item.Contested, item.OptIn, item.LaptopSensitive, item.RestartsExplorer, item.Condition));
            Assert.Equal(UserScopeSettingsItems.Contains(item.Id) ? ItemScope.User : ItemScope.Machine, item.Scope);
        });
    }

    [Fact]
    public void Only_the_listed_items_are_opt_in_restart_Explorer_or_have_a_condition()
    {
        var items = CleanupCatalog.LoadEmbedded().Items;
        string[] Want(Func<Expected, bool> pick) => SettingsItems.Where(pick).Select(e => e.Id).Order(StringComparer.Ordinal).ToArray();
        IEnumerable<string> Have(Func<CleanupItem, bool> pick) => items.Where(pick).Select(i => i.Id).Order(StringComparer.Ordinal);

        Assert.Equal(Want(e => e.OptIn), Have(i => i.OptIn));
        Assert.Equal(Want(e => e.RestartsExplorer), Have(i => i.RestartsExplorer));
        Assert.Equal(Want(e => e.Condition is not null), Have(i => i.Condition is not null));
    }

    [Theory]
    [InlineData("apps.widgets", "MicrosoftWindows.Client.WebExperience")]
    [InlineData("apps.outlook-new", "Microsoft.OutlookForWindows")]
    [InlineData("apps.bing-search", "Microsoft.BingSearch")]
    [InlineData("apps.copilot", "Microsoft.Copilot")]
    [InlineData("apps.office-hub", "Microsoft.MicrosoftOfficeHub")]
    [InlineData("apps.phone-link", "Microsoft.YourPhone")]
    public void App_items_remove_the_spec_package(string id, string package)
    {
        var appx = Assert.Single(CleanupCatalog.LoadEmbedded().Find(id)!.Actions.OfType<AppxAction>());
        Assert.Equal(package, appx.Package);
        Assert.Matches("^[0-9A-Z]{12,14}$", appx.StoreId);
    }

    [Fact]
    public void Widgets_item_also_sets_the_widgets_policy()
    {
        var registry = Assert.Single(CleanupCatalog.LoadEmbedded().Find("apps.widgets")!.Actions.OfType<RegistryAction>());

        Assert.Equal((@"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests"), (registry.Path, registry.Name));
        Assert.Equal(0, registry.Value!.Value.GetInt32());
    }

    [Fact]
    public void Telemetry_task_items_disable_the_spec_tasks()
    {
        var catalog = CleanupCatalog.LoadEmbedded();
        var tasks = catalog.Find("privacy.telemetry-tasks")!.Actions.Cast<ScheduledTaskAction>().ToList();

        Assert.Equal(TelemetryTasks.Order(StringComparer.Ordinal), tasks.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(tasks, t => Assert.False(t.Enabled));
        var appraiser = Assert.IsType<ScheduledTaskAction>(Assert.Single(catalog.Find("privacy.compatibility-appraiser")!.Actions));
        Assert.Equal((@"\Microsoft\Windows\Application Experience\", "Microsoft Compatibility Appraiser Exp", false),
            (appraiser.Path, appraiser.Name, appraiser.Enabled));
    }

    [Fact]
    public void Power_items_target_the_spec_settings()
    {
        var catalog = CleanupCatalog.LoadEmbedded();
        PowerSettingAction Setting(string id) => Assert.IsType<PowerSettingAction>(Assert.Single(catalog.Find(id)!.Actions));
        const string display = "7516b95f-f776-4464-8c53-06167f40cc99", videoIdle = "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e";

        Assert.Equal(new PowerSettingAction(display, videoIdle, 900, null), Setting("power.monitor-off-15"));
        Assert.Equal(new PowerSettingAction(display, videoIdle, 1800, null), Setting("power.monitor-off-30"));
        Assert.Equal(new PowerSettingAction("238c9fa8-0aad-41ed-83f4-97be242c8f20", "29f6c1db-86da-48c5-9fdb-f2b67b1f44da", 0, null), Setting("power.sleep-never"));
        Assert.Equal("formatkit", Assert.IsType<PowerSchemeAction>(Assert.Single(catalog.Find("performance.formatkit-plan")!.Actions)).Scheme);
        var catalogOrder = catalog.Items.Select(i => i.Id).ToList();
        Assert.True(catalogOrder.IndexOf("power.monitor-off-15") < catalogOrder.IndexOf("power.monitor-off-30"));
    }

    [Theory]
    [InlineData("input.ptbr-us-international", "keyboardLayout")]
    [InlineData("sound.no-sounds", "soundScheme")]
    [InlineData("power.hibernation-off", "hibernate")]
    public void Named_handler_items_use_their_handler(string id, string handler) =>
        Assert.Equal(handler, Assert.IsType<NamedAction>(Assert.Single(CleanupCatalog.LoadEmbedded().Find(id)!.Actions)).Handler);

    [Fact]
    public void Every_item_says_what_it_breaks_and_contested_items_say_no_gain_is_documented()
    {
        var items = CleanupCatalog.LoadEmbedded().Items;

        Assert.All(items, item => Assert.All(item.Text.Values, text => Assert.False(string.IsNullOrWhiteSpace(text.Breaks))));
        Assert.All(items.Where(i => i.Contested), item =>
        {
            Assert.Equal("No gain is documented by Microsoft.", item.Text["en"].Gain);
            Assert.Equal("A Microsoft não documenta nenhum ganho.", item.Text["pt-BR"].Gain);
        });
    }

    [Fact]
    public void Known_issues_are_valid() =>
        Assert.Empty(KnownIssuesValidator.Validate(KnownIssues.LoadEmbedded()));

    [Fact]
    public void Every_protected_service_has_a_rule()
    {
        var ruled = KnownIssues.LoadEmbedded().Issues
            .Select(i => i.Rule).OfType<ServiceDisabledRule>()
            .Select(r => r.Service)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = ProtectedLists.Services
            .Except(DisabledByDefaultServices, StringComparer.OrdinalIgnoreCase)
            .Where(s => !ruled.Contains(s));
        Assert.Empty(missing);
        Assert.All(DisabledByDefaultServices, s => Assert.DoesNotContain(s, ruled));
    }

    [Fact]
    public void Every_not_included_tweak_detectable_in_registry_has_a_rule()
    {
        var rules = KnownIssues.LoadEmbedded().Issues.Select(i => i.Rule).ToList();

        var registry = rules.OfType<RegistryValueRule>().ToList();
        Assert.All(NotIncludedRegistryTweaks, tweak => Assert.Contains(registry, r =>
            string.Equals(r.Path, tweak.Path, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Name, tweak.Name, StringComparison.OrdinalIgnoreCase)));

        var devices = rules.OfType<DeviceDisabledRule>().Select(r => r.HardwareId).ToList();
        Assert.All(NotIncludedDevices, id => Assert.Contains(id, devices, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_not_included_service_has_a_rule_and_only_SysMain_is_informational()
    {
        var rules = KnownIssues.LoadEmbedded().Issues
            .Where(i => i.Rule is ServiceDisabledRule)
            .ToDictionary(i => ((ServiceDisabledRule)i.Rule).Service, StringComparer.OrdinalIgnoreCase);

        Assert.All(NotIncludedServices, service =>
        {
            Assert.True(rules.TryGetValue(service, out var issue), service + " has no rule");
            Assert.Equal(service == "SysMain" ? IssueSeverity.Info : IssueSeverity.Breaks, issue.Severity);
        });
    }

    [Theory]
    [InlineData(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", "0")]
    [InlineData(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", "2")]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents", "255")]
    public void Not_included_registry_rules_match_only_the_documented_value(string path, string name, string value)
    {
        var rule = Assert.Single(KnownIssues.LoadEmbedded().Issues.Select(i => i.Rule).OfType<RegistryValueRule>(),
            r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase) && r.Name == name);

        Assert.Equal(value, rule.EqualsValue);
        Assert.Null(rule.NotEqualsValue);
    }

    /// <summary>Texts the Task 10 review asked to say what the item really changes.</summary>
    [Theory]
    [InlineData("apps.bing-search", "web results", "resultados da web")]
    [InlineData("apps.copilot", "work or school", "trabalho ou escola")]
    [InlineData("apps.office-hub", "work or school", "trabalho ou escola")]
    [InlineData("privacy.start-web-search", "blank", "em branco")]
    [InlineData("input.sticky-keys-shortcut-off", "turns it off", "desliga")]
    public void Breaks_text_names_the_real_effect(string id, string en, string pt)
    {
        var item = CleanupCatalog.LoadEmbedded().Find(id)!;

        Assert.Contains(en, item.Text["en"].Breaks, StringComparison.Ordinal);
        Assert.Contains(pt, item.Text["pt-BR"].Breaks, StringComparison.Ordinal);
    }
}
