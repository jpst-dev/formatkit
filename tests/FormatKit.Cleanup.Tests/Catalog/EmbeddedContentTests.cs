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
    ];

    /// <summary>
    /// Items left out because Microsoft documentation could not confirm their location or the app cannot be
    /// reinstalled. See the commit "feat(cleanup): add initial catalog and known-issue rules".
    /// </summary>
    private static readonly string[] Dropped =
    [
        "apps.tips",
        "privacy.start-suggestions", "privacy.lock-screen-tips", "privacy.settings-suggestions",
        "privacy.tips-notifications", "privacy.silent-app-installs",
        "performance.gpu-scheduling", "performance.network-throttling", "performance.game-bar-recording",
    ];

    /// <summary>The contested items of spec 7.3 that survived verification.</summary>
    private static readonly string[] ContestedItemIds =
    [
        "performance.system-responsiveness", "performance.games-task-priority",
        "performance.priority-separation", "performance.power-throttling-off",
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

    [Fact]
    public void Every_contested_item_has_a_registry_value_rule()
    {
        var rules = KnownIssues.LoadEmbedded().Issues.Select(i => i.Rule).OfType<RegistryValueRule>().ToList();

        Assert.All(CleanupCatalog.LoadEmbedded().Items.Where(i => i.Contested), item => Assert.Contains(rules, rule =>
            item.Actions.OfType<RegistryAction>().Any(action =>
                string.Equals(action.Path, rule.Path, StringComparison.OrdinalIgnoreCase)
                && string.Equals(action.Name, rule.Name, StringComparison.OrdinalIgnoreCase))));
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
}
