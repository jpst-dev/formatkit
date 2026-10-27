using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class ServiceControlTests
{
    private const string W32Time = "HKLM\\SYSTEM\\CurrentControlSet\\Services\\W32Time";

    private static ProcessResult Result(int exit, string stdout = "", string stderr = "") =>
        new(exit, stdout, stderr, TimeSpan.Zero, false);

    [Theory]
    [InlineData("2", ServiceStartType.Automatic)]
    [InlineData("3", ServiceStartType.Manual)]
    [InlineData("4", ServiceStartType.Disabled)]
    [InlineData("1", ServiceStartType.Automatic)]
    public void GetStartType_maps_registry_start_values(string start, ServiceStartType expected)
    {
        var registry = new FakeRegistry();
        registry.Set(W32Time, "Start", RegistryKind.Dword, start);

        Assert.Equal(expected, new ServiceControl(registry, new FakeProcessRunner()).GetStartType("W32Time"));
    }

    [Fact]
    public void GetStartType_of_missing_service_is_null()
    {
        Assert.Null(new ServiceControl(new FakeRegistry(), new FakeProcessRunner()).GetStartType("Nope"));
    }

    [Fact]
    public async Task SetStartType_runs_sc_config()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("sc.exe", Result(0));

        await new ServiceControl(new FakeRegistry(), runner)
            .SetStartTypeAsync("W32Time", ServiceStartType.Manual, TestContext.Current.CancellationToken);

        var call = Assert.Single(runner.Calls);
        Assert.Equal("sc.exe", call.FileName);
        Assert.Equal(["config", "W32Time", "start=", "demand"], call.Arguments);
    }

    [Fact]
    public async Task SetStartType_failure_throws()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("sc.exe", Result(1060));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ServiceControl(new FakeRegistry(), runner)
                .SetStartTypeAsync("Nope", ServiceStartType.Disabled, TestContext.Current.CancellationToken));

        Assert.Contains("1060", ex.Message);
    }
}
