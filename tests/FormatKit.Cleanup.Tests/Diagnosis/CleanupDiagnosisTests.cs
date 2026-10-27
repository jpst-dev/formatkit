using FormatKit.Cleanup.Diagnosis;
using FormatKit.Cleanup.Journal;
using FormatKit.Core;
using FormatKit.Core.Hardware;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Cleanup.Tests.Diagnosis;

public class CleanupDiagnosisTests
{
    private const string Hvci = @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    private const string GameBar = @"HKCU\Software\Microsoft\GameBar";
    private const string Profile = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string Interfaces = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly InMemoryFileSystem _files = new();
    private readonly AppPaths _paths = new(@"C:\FormatKitTest");
    private readonly FakeRegistry _registry = new();
    private readonly FakeServiceControl _services = new();
    private readonly FakePowerSchemes _power = new();
    private readonly FakeHardwareInfo _hardware = new(FakeHardwareInfo.Sample());

    private static IssueText T(string s) => new(s, s + " breaks", s + " fix");

    private static KnownIssue Issue(string id, IssueSeverity severity, IssueRule rule) =>
        new(id, severity, rule, new Dictionary<string, IssueText> { ["en"] = T(id), ["pt-BR"] = T(id) });

    private static KnownIssues Rules(params KnownIssue[] issues) => new(1, issues);

    private static readonly KnownIssues AuthorRules = Rules(
        Issue("device-vdrv", IssueSeverity.Breaks, new DeviceDisabledRule("ROOT\\VDRVROOT")),
        Issue("device-umbus", IssueSeverity.Breaks, new DeviceDisabledRule("root\\umbus")),
        Issue("device-ndis", IssueSeverity.Info, new DeviceDisabledRule("ROOT\\NDISVIRTUALBUS")),
        Issue("device-hpet", IssueSeverity.Info, new DeviceDisabledRule("ACPI\\PNP0103")),
        Issue("svc-w32time", IssueSeverity.Breaks, new ServiceDisabledRule("W32Time")),
        Issue("svc-defrag", IssueSeverity.Info, new ServiceDisabledRule("defragsvc")),
        Issue("svc-xbox", IssueSeverity.Info, new ServiceDisabledRule("XboxGipSvc")),
        Issue("svc-ssh", IssueSeverity.Security, new ServiceDisabledRule("ssh-agent")),
        Issue("reg-hvci", IssueSeverity.Security, new RegistryValueRule(Hvci, "Enabled", "0", null)),
        Issue("reg-gamemode", IssueSeverity.Contested, new RegistryValueRule(GameBar, "AutoGameModeEnabled", "0", null)),
        Issue("reg-sysresp", IssueSeverity.Contested, new RegistryValueRule(Profile, "SystemResponsiveness", null, "20")),
        Issue("reg-throttle", IssueSeverity.Contested, new RegistryValueRule(Profile, "NetworkThrottlingIndex", "4294967295", null)),
        Issue("power-custom", IssueSeverity.Info, new NonBuiltInPowerSchemeRule()));

    private Task<DiagnosisReport> Run(KnownIssues rules) =>
        new CleanupDiagnosis(rules, _paths).RunAsync(
            TestServices.Create(files: _files, registry: _registry, services: _services, power: _power, hardware: _hardware), Ct);

    private static ProblemDevice Device(string name, params string[] ids) => new(name, "", 22, ids);

    [Fact]
    public async Task Author_pc_reproduction()
    {
        _hardware.DisabledDevices =
        [
            Device("Virtual Driver Root", @"ROOT\VDRVROOT", @"ROOT\VDRVROOT\0000"),
            Device("UMBus Root Bus Enumerator", @"ROOT\UMBUS"),
            Device("NDIS Virtual Network Adapter Enumerator", @"ROOT\NDISVIRTUALBUS"),
            Device("High precision event timer", @"ACPI\PNP0103"),
            Device("Unrelated", @"PCI\VEN_1234"),
        ];
        foreach (var s in new[] { "W32Time", "defragsvc", "XboxGipSvc", "ssh-agent" })
            _services.Services[s] = ServiceStartType.Disabled;
        _registry.Set(Hvci, "Enabled", RegistryKind.Dword, "0");
        _registry.Set(GameBar, "AutoGameModeEnabled", RegistryKind.Dword, "0");
        _registry.Set(Profile, "SystemResponsiveness", RegistryKind.Dword, "0");
        _registry.Set(Profile, "NetworkThrottlingIndex", RegistryKind.Dword, "4294967295");
        _power.Active = Guid.Empty;

        var report = await Run(AuthorRules);

        Assert.Equal(
            [
                "device-umbus", "device-vdrv", "svc-w32time",
                "reg-hvci", "svc-ssh",
                "reg-gamemode", "reg-sysresp", "reg-throttle",
                "device-hpet", "device-ndis", "power-custom", "svc-defrag", "svc-xbox",
            ],
            report.Findings.Select(f => f.Issue.Id));
        Assert.Equal(
            [IssueSeverity.Breaks, IssueSeverity.Breaks, IssueSeverity.Breaks, IssueSeverity.Security, IssueSeverity.Security,
             IssueSeverity.Contested, IssueSeverity.Contested, IssueSeverity.Contested,
             IssueSeverity.Info, IssueSeverity.Info, IssueSeverity.Info, IssueSeverity.Info, IssueSeverity.Info],
            report.Findings.Select(f => f.Issue.Severity));
        string Detail(string id) => report.Findings.Single(f => f.Issue.Id == id).Detail;
        Assert.Equal("Virtual Driver Root", Detail("device-vdrv"));
        Assert.Equal("W32Time", Detail("svc-w32time"));
        Assert.Equal(Hvci, Detail("reg-hvci"));
        Assert.Equal(Profile, Detail("reg-sysresp"));
        Assert.Equal("00000000-0000-0000-0000-000000000000", Detail("power-custom"));
        Assert.Empty(report.Applied);
        Assert.Null(report.JournalError);
    }

    [Fact]
    public async Task Clean_system_has_no_findings()
    {
        _services.Services["W32Time"] = ServiceStartType.Automatic;
        _registry.Set(Hvci, "Enabled", RegistryKind.Dword, "1");
        _registry.Set(Profile, "SystemResponsiveness", RegistryKind.Dword, "20");
        _hardware.DisabledDevices = [Device("Unrelated", @"PCI\VEN_1234")];

        var report = await Run(AuthorRules);

        Assert.Empty(report.Findings);
        Assert.Empty(report.Applied);
        Assert.Null(report.JournalError);
    }

    [Fact]
    public async Task Absent_registry_value_never_matches()
    {
        var report = await Run(Rules(
            Issue("a", IssueSeverity.Info, new RegistryValueRule(Hvci, "Enabled", "0", null)),
            Issue("b", IssueSeverity.Info, new RegistryValueRule(Hvci, "Enabled", null, "1"))));

        Assert.Empty(report.Findings);
    }

    [Fact]
    public async Task Wildcard_path_matches_each_interface()
    {
        _registry.Set(Interfaces + @"\{aaa}", "TcpAckFrequency", RegistryKind.Dword, "1");
        _registry.Set(Interfaces + @"\{bbb}", "TcpAckFrequency", RegistryKind.Dword, "1");
        _registry.Set(Interfaces + @"\{ccc}", "TcpAckFrequency", RegistryKind.Dword, "2");

        var report = await Run(Rules(
            Issue("nagle", IssueSeverity.Contested, new RegistryValueRule(Interfaces + @"\*", "TcpAckFrequency", "1", null))));

        Assert.Equal([Interfaces + @"\{aaa}", Interfaces + @"\{bbb}"], report.Findings.Select(f => f.Detail).Order());
    }

    [Fact]
    public async Task Builtin_scheme_and_non_disabled_services_do_not_match()
    {
        _power.Active = BuiltInPowerSchemes.Ultimate;
        _services.Services["W32Time"] = ServiceStartType.Manual;

        var report = await Run(Rules(
            Issue("p", IssueSeverity.Info, new NonBuiltInPowerSchemeRule()),
            Issue("s", IssueSeverity.Info, new ServiceDisabledRule("W32Time")),
            Issue("missing", IssueSeverity.Info, new ServiceDisabledRule("NoSuchService"))));

        Assert.Empty(report.Findings);
    }

    [Fact]
    public async Task Applied_lists_each_item_with_unreverted_entries_once()
    {
        var now = DateTimeOffset.UtcNow;
        JournalEntry Entry(string id, DateTimeOffset? reverted) => new(id, now, [], reverted);
        new JournalStore(_files, _paths).Save(new CleanupJournal(1,
            [Entry("privacy.a", null), Entry("privacy.a", null), Entry("privacy.b", now), Entry("apps.c", null)]));

        var report = await Run(Rules());

        Assert.Equal(["privacy.a", "apps.c"], report.Applied.Select(a => a.ItemId));
        Assert.All(report.Applied, a => Assert.True(a.CanUndo));
        Assert.Null(report.JournalError);
    }

    [Fact]
    public async Task Corrupt_journal_reports_error_and_still_diagnoses()
    {
        _files.Seed(_paths.CleanupJournalFile, "{ not json");
        _services.Services["W32Time"] = ServiceStartType.Disabled;

        var report = await Run(AuthorRules);

        Assert.NotNull(report.JournalError);
        Assert.Empty(report.Applied);
        Assert.Equal(["svc-w32time"], report.Findings.Select(f => f.Issue.Id));
    }

    [Fact]
    public async Task Diagnosis_never_writes()
    {
        _services.Services["W32Time"] = ServiceStartType.Disabled;
        _registry.Set(Hvci, "Enabled", RegistryKind.Dword, "0");
        var before = _registry.Values.ToDictionary(kv => kv.Key, kv => kv.Value);

        await Run(AuthorRules);

        Assert.Equal(before, _registry.Values.ToDictionary(kv => kv.Key, kv => kv.Value));
        Assert.Equal(0, _files.WriteCount);
    }

    [Fact]
    public void Embedded_known_issues_load() =>
        Assert.Equal(1, KnownIssues.LoadEmbedded().SchemaVersion);

    [Fact]
    public void Parse_reads_polymorphic_rules_and_equals_names()
    {
        var json = """
            { "schemaVersion": 1, "issues": [
              { "id": "a", "severity": "security",
                "rule": { "kind": "registryValue", "path": "HKLM\\X\\*", "name": "N", "notEquals": "1" },
                "text": { "en": { "title": "t", "breaks": "b", "fix": "f" } } },
              { "id": "b", "severity": "info", "rule": { "kind": "nonBuiltInPowerScheme" }, "text": {} } ] }
            """;

        var issues = KnownIssues.Parse(json);

        var rule = Assert.IsType<RegistryValueRule>(issues.Issues[0].Rule);
        Assert.Null(rule.EqualsValue);
        Assert.Equal("1", rule.NotEqualsValue);
        Assert.Equal(IssueSeverity.Security, issues.Issues[0].Severity);
        Assert.IsType<NonBuiltInPowerSchemeRule>(issues.Issues[1].Rule);
    }
}
