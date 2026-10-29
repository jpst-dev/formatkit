using FormatKit.App.Localization;
using FormatKit.App.ViewModels;
using FormatKit.Cleanup.Diagnosis;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.App.Tests;

public class CleanupDiagnosisViewModelTests
{
    private static IssueText T(string s) => new(s, s + " breaks", s + " fix");

    private static KnownIssue Issue(string id, IssueSeverity severity, string service) =>
        new(id, severity, new ServiceDisabledRule(service),
            new Dictionary<string, IssueText> { ["en"] = T(id), ["pt-BR"] = T(id) });

    private static CleanupDiagnosisViewModel Create(KnownIssues issues, params string[] disabledServices)
    {
        var services = new FakeServiceControl();
        foreach (var name in disabledServices)
            services.Services[name] = ServiceStartType.Disabled;
        return new CleanupDiagnosisViewModel(TestServices.Create(services: services), issues);
    }

    [Fact]
    public async Task Check_GroupsBySeverityInCliOrder()
    {
        var issues = new KnownIssues(1,
        [
            Issue("a-info", IssueSeverity.Info, "InfoSvc"),
            Issue("b-security", IssueSeverity.Security, "SecSvc"),
            Issue("c-breaks", IssueSeverity.Breaks, "BreakSvc"),
            Issue("d-breaks", IssueSeverity.Breaks, "Break2Svc"),
        ]);
        var vm = Create(issues, "InfoSvc", "SecSvc", "BreakSvc", "Break2Svc");

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.NoFindings);
        Assert.Equal([IssueSeverity.Breaks, IssueSeverity.Security, IssueSeverity.Info], vm.Groups.Select(g => g.Severity));
        Assert.Equal(Strings.Get("Check_Group_Breaks"), vm.Groups[0].Title);
        var breaks = vm.Groups[0].Findings;
        Assert.Equal(["c-breaks", "d-breaks"], breaks.Select(f => f.Title));
        Assert.Equal("BreakSvc", breaks[0].Detail);
        Assert.Equal(Strings.Format("Check_Breaks", "c-breaks breaks"), breaks[0].Breaks);
        Assert.Equal(Strings.Format("Check_Fix", "c-breaks fix"), breaks[0].Fix);
    }

    [Fact]
    public async Task Check_NoFindings_SetsNoFindings()
    {
        var vm = Create(new KnownIssues(1, [Issue("a", IssueSeverity.Breaks, "Svc")]));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.NoFindings);
        Assert.Empty(vm.Groups);
        Assert.Null(vm.Error);
    }
}
