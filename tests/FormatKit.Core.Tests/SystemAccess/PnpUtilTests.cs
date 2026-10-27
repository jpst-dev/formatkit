using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class PnpUtilTests
{
    private static ProcessResult Result(int exit, bool timedOut = false) =>
        new(exit, "out", "", TimeSpan.Zero, timedOut);

    [Fact]
    public async Task Add_drivers_uses_subdirs_and_install()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("pnputil.exe", Result(0));

        var result = await new PnpUtil(runner).AddDriversAsync("D:\\Drivers", TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.False(result.RebootRequired);
        Assert.Equal(["/add-driver", "D:\\Drivers\\*.inf", "/subdirs", "/install"], runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task Export_driver_passes_inf_and_dir()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("pnputil.exe", Result(0));

        await new PnpUtil(runner).ExportDriverAsync("oem12.inf", "D:\\out", TestContext.Current.CancellationToken);

        Assert.Equal(["/export-driver", "oem12.inf", "D:\\out"], runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task Exit_3010_means_success_with_reboot()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("pnputil.exe", Result(3010));

        var result = await new PnpUtil(runner).AddDriversAsync("D:\\Drivers", TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(result.RebootRequired);
    }

    [Fact]
    public async Task Timeout_is_failure()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("pnputil.exe", Result(0, timedOut: true));

        var result = await new PnpUtil(runner).AddDriversAsync("D:\\Drivers", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
    }
}
