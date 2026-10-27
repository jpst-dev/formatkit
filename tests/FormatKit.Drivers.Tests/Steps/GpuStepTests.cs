using FormatKit.Core.Hardware;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Drivers.Catalog;
using FormatKit.Drivers.Steps;

namespace FormatKit.Drivers.Tests.Steps;

public class GpuStepTests
{
    private static readonly VendorCatalog Catalog = VendorCatalog.LoadEmbedded();

    private static readonly GpuInfo Nvidia = Gpu("RTX", "PCI\\VEN_10DE&DEV_1", GpuVendor.Nvidia);
    private static readonly GpuInfo Intel = Gpu("UHD", "PCI\\VEN_8086&DEV_1", GpuVendor.Intel);

    private static ProcessResult Result(int exit) => new(exit, "", "", TimeSpan.Zero, false);
    private static GpuInfo Gpu(string name, string pnp, GpuVendor vendor) => new(name, pnp, vendor, null);

    private static (GpuStep Step, FakeProcessRunner Runner, StepContext Ctx) Arrange(params GpuInfo[] gpus)
    {
        var runner = new FakeProcessRunner();
        var hw = new FakeHardwareInfo(FakeHardwareInfo.Sample(gpus: gpus));
        return (new GpuStep(Catalog), runner, TestServices.Context(TestServices.Create(runner, hw)));
    }

    [Fact]
    public void Has_expected_metadata()
    {
        var step = new GpuStep(Catalog);

        Assert.Equal("drivers.gpu", step.Id);
        Assert.True(step.RequiresAdmin);
        Assert.False(string.IsNullOrWhiteSpace(step.Title));
    }

    [Fact]
    public async Task Skips_when_no_known_gpu()
    {
        var (step, runner, ctx) = Arrange(Gpu("Basic", "ROOT\\BasicDisplay", GpuVendor.Unknown));

        Assert.True(await step.CheckAsync(ctx, TestContext.Current.CancellationToken));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Amd_only_gpu_is_skipped()
    {
        var (step, runner, ctx) = Arrange(Gpu("Radeon", "PCI\\VEN_1002&DEV_1", GpuVendor.Amd));

        Assert.True(await step.CheckAsync(ctx, TestContext.Current.CancellationToken));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Skips_when_package_already_installed()
    {
        var (step, runner, ctx) = Arrange(Nvidia);
        runner.Enqueue("winget.exe", Result(0));

        Assert.True(await step.CheckAsync(ctx, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Check_false_when_package_missing()
    {
        var (step, runner, ctx) = Arrange(Nvidia);
        runner.Enqueue("winget.exe", Result(1));

        Assert.False(await step.CheckAsync(ctx, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Installs_missing_package()
    {
        var (step, runner, ctx) = Arrange(Nvidia);
        runner.Enqueue("winget.exe", Result(1)); // list: not installed
        runner.Enqueue("winget.exe", Result(0)); // install

        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        var install = runner.Calls[1].Arguments;
        Assert.Equal("install", install[0]);
        Assert.Contains("XP8CLZL93F5Z4P", install);
        Assert.Equal(["--source", "msstore"], install.TakeLast(2));
        Assert.Equal(["--source", "msstore"], runner.Calls[0].Arguments.TakeLast(2));
    }

    [Fact]
    public async Task Hybrid_intel_and_nvidia_installs_both()
    {
        var (step, runner, ctx) = Arrange(Intel, Nvidia);
        runner.Enqueue("winget.exe", Result(1));
        runner.Enqueue("winget.exe", Result(0));
        runner.Enqueue("winget.exe", Result(1));
        runner.Enqueue("winget.exe", Result(0));

        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        var installs = runner.Calls.Where(c => c.Arguments[0] == "install").ToList();
        Assert.Equal(2, installs.Count);
        Assert.Contains(installs, c => c.Arguments.Contains("Intel.IntelDriverAndSupportAssistant"));
        Assert.Contains(installs, c => c.Arguments.Contains("XP8CLZL93F5Z4P"));
    }

    [Fact]
    public async Task Duplicate_gpus_install_once()
    {
        var (step, runner, ctx) = Arrange(Nvidia, Nvidia);
        runner.Enqueue("winget.exe", Result(1));
        runner.Enqueue("winget.exe", Result(0));

        await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task Failed_install_fails_listing_package()
    {
        var (step, runner, ctx) = Arrange(Nvidia);
        runner.Enqueue("winget.exe", Result(1));
        runner.Enqueue("winget.exe", Result(1));

        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains(result.Messages, m => m.Contains("XP8CLZL93F5Z4P"));
    }
}
