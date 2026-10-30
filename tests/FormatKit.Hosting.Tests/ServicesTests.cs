using FormatKit.Cli;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Hosting.Tests;

public sealed class ServicesTests
{
    [Fact]
    public void ToHost_ThenFromHost_KeepsEveryField()
    {
        var files = new InMemoryFileSystem();
        var host = new HostEnvironment(
            TestServices.Create(files: files),
            new Elevation(),
            new FakeScheduledTasks(),
            new FakeRebooter(),
            new FakeReportWriter(),
            new RunStateStore(files, new AppPaths("data")),
            Serilog.Core.Logger.None,
            TimeSpan.FromSeconds(7),
            TimeSpan.FromSeconds(3),
            SimulateRebootAfterStep: "step-x",
            Dev: null);
        var output = new StringWriter();

        var cli = CliEnvironment.FromHost(host, output);
        var back = cli.ToHost();

        Assert.Same(output, cli.Out);
        Assert.Equal(host, back);
    }
}
