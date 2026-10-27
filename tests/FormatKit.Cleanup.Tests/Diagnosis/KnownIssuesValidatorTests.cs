using FormatKit.Cleanup.Diagnosis;

namespace FormatKit.Cleanup.Tests.Diagnosis;

public class KnownIssuesValidatorTests
{
    private static IssueText Text(string s = "x") => new(s, s, s);

    private static KnownIssue Issue(string id = "a", IssueRule? rule = null, IssueText? en = null, bool dropPt = false)
    {
        var text = new Dictionary<string, IssueText> { ["en"] = en ?? Text() };
        if (!dropPt)
            text["pt-BR"] = Text();
        return new KnownIssue(id, IssueSeverity.Info, rule ?? new ServiceDisabledRule("W32Time"), text);
    }

    private static IReadOnlyList<string> Validate(params KnownIssue[] issues) =>
        KnownIssuesValidator.Validate(new KnownIssues(1, issues));

    [Fact]
    public void Valid_issues_pass() =>
        Assert.Empty(Validate(
            Issue("a"),
            Issue("b", new DeviceDisabledRule("ROOT\\UMBUS")),
            Issue("c", new RegistryValueRule(@"HKLM\X\*", "N", "1", null)),
            Issue("d", new RegistryValueRule(@"HKLM\X", "N", null, "1")),
            Issue("e", new NonBuiltInPowerSchemeRule())));

    [Fact]
    public void Duplicate_id_is_rejected() =>
        Assert.Contains(Validate(Issue("a"), Issue("a")), e => e.Contains("duplicate id"));

    [Fact]
    public void Missing_language_is_rejected() =>
        Assert.Contains(Validate(Issue(dropPt: true)), e => e.Contains("'pt-BR'"));

    [Theory]
    [InlineData("title")]
    [InlineData("breaks")]
    [InlineData("fix")]
    public void Blank_field_is_rejected(string field)
    {
        var blank = field switch
        {
            "title" => new IssueText(" ", "b", "f"),
            "breaks" => new IssueText("t", "", "f"),
            _ => new IssueText("t", "b", " "),
        };
        Assert.Contains(Validate(Issue(en: blank)), e => e.Contains($"'en' field '{field}'"));
    }

    [Fact]
    public void Device_rule_needs_hardware_id() =>
        Assert.Contains(Validate(Issue(rule: new DeviceDisabledRule(" "))), e => e.Contains("hardwareId"));

    [Fact]
    public void Service_rule_needs_service() =>
        Assert.Contains(Validate(Issue(rule: new ServiceDisabledRule(""))), e => e.Contains("serviceDisabled"));

    [Fact]
    public void Registry_rule_needs_exactly_one_comparison()
    {
        Assert.Contains(Validate(Issue(rule: new RegistryValueRule(@"HKLM\X", "N", null, null))), e => e.Contains("exactly one"));
        Assert.Contains(Validate(Issue(rule: new RegistryValueRule(@"HKLM\X", "N", "1", "2"))), e => e.Contains("exactly one"));
    }

    [Fact]
    public void Registry_rule_allows_at_most_one_wildcard() =>
        Assert.Contains(Validate(Issue(rule: new RegistryValueRule(@"HKLM\*\X\*", "N", "1", null))), e => e.Contains("wildcard"));

    [Fact]
    public void Registry_rule_needs_path_and_name() =>
        Assert.Contains(Validate(Issue(rule: new RegistryValueRule("", "N", "1", null))), e => e.Contains("path and a name"));
}
