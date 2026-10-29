using FormatKit.App.Localization;
using FormatKit.App.ViewModels;
using FormatKit.Cleanup.Diagnosis;
using FormatKit.Core;
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

    private sealed class ThrowingServiceControl : IServiceControl
    {
        public ServiceStartType? GetStartType(string name) => throw new InvalidOperationException("unreadable");

        public Task SetStartTypeAsync(string name, ServiceStartType type, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task Check_RuleThrows_CountsNotCheckedAndIsNotNoFindings()
    {
        var vm = new CleanupDiagnosisViewModel(
            TestServices.Create() with { ServiceControl = new ThrowingServiceControl() },
            new KnownIssues(1, [Issue("a", IssueSeverity.Breaks, "Svc")]));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.NotChecked);
        Assert.True(vm.HasNotChecked);
        Assert.Equal(Strings.Format("Check_NotChecked", 1), vm.NotCheckedText);
        Assert.False(vm.NoFindings);
    }

    [Fact]
    public async Task Check_CorruptJournal_SetsJournalError()
    {
        var files = new InMemoryFileSystem();
        files.Seed(AppPaths.Default.CleanupJournalFile, "{ not json");
        var vm = new CleanupDiagnosisViewModel(
            TestServices.Create(files: files), new KnownIssues(1, [Issue("a", IssueSeverity.Breaks, "Svc")]));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(vm.JournalError);
        Assert.True(vm.HasJournalError);
        Assert.Equal(Strings.Format("Check_JournalError", vm.JournalError!), vm.JournalErrorText);
    }

    private sealed class FailingSession : IUserSession
    {
        public bool Fail { get; set; }

        public bool IsConsoleUser => true;

        public bool HasInteractiveUser => true;

        public string? CurrentUserSid =>
            Fail ? throw new System.Runtime.InteropServices.COMException("access refused") : "S-1-5-21-1";
    }

    [Fact]
    public async Task Refresh_Error_ClearsStaleResults()
    {
        var services = new FakeServiceControl();
        services.Services["Svc"] = ServiceStartType.Disabled;
        var session = new FailingSession();
        var vm = new CleanupDiagnosisViewModel(
            TestServices.Create(services: services) with { UserSession = session },
            new KnownIssues(1, [Issue("a", IssueSeverity.Breaks, "Svc")]));
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Groups);

        session.Fail = true;
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Error);
        Assert.Empty(vm.Groups);
        Assert.False(vm.NoFindings);
    }
}
