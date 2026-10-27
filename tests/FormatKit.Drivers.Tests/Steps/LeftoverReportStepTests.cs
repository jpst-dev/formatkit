using FormatKit.Core.Hardware;
using FormatKit.Core.Steps;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Drivers.Catalog;
using FormatKit.Drivers.Steps;

namespace FormatKit.Drivers.Tests.Steps;

public class LeftoverReportStepTests
{
    private static readonly VendorCatalog Catalog = VendorCatalog.LoadEmbedded();

    private static StepContext Arrange(FakeHardwareInfo hw) => TestServices.Context(TestServices.Create(hardware: hw));

    [Fact]
    public async Task Has_expected_metadata_and_never_skips()
    {
        var step = new LeftoverReportStep(Catalog);
        var ctx = Arrange(new FakeHardwareInfo(FakeHardwareInfo.Sample()));

        Assert.Equal("drivers.leftover-report", step.Id);
        Assert.False(step.RequiresAdmin);
        Assert.False(string.IsNullOrWhiteSpace(step.Title));
        Assert.False(await step.CheckAsync(ctx, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task No_problem_devices_reports_all_good()
    {
        var ctx = Arrange(new FakeHardwareInfo(FakeHardwareInfo.Sample()));

        var result = await new LeftoverReportStep(Catalog).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Single(result.Messages);
        Assert.DoesNotContain("Search.aspx", result.Messages[0]);
    }

    [Fact]
    public async Task Lists_device_with_catalog_link()
    {
        var device = new ProblemDevice("Unknown device", "PCI\\X", 28, ["PCI\\VEN_8086&DEV_A0F0", "PCI\\VEN_8086"]);
        var ctx = Arrange(new FakeHardwareInfo(FakeHardwareInfo.Sample(problemDevices: [device])));

        var result = await new LeftoverReportStep(Catalog).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        var message = Assert.Single(result.Messages);
        Assert.Contains("Unknown device", message);
        Assert.Contains("28", message);
        Assert.Contains("PCI\\VEN_8086&DEV_A0F0", message);
        Assert.Contains("Search.aspx?q=PCI%5CVEN_8086%26DEV_A0F0", message);
    }

    [Fact]
    public async Task Custom_build_adds_board_support_url()
    {
        var device = new ProblemDevice("Unknown device", "PCI\\X", 28, ["PCI\\VEN_8086&DEV_A0F0"]);
        var hw = FakeHardwareInfo.Sample(boardManufacturer: "ASUSTeK COMPUTER INC.", problemDevices: [device]);
        var ctx = Arrange(new FakeHardwareInfo(hw));

        var result = await new LeftoverReportStep(Catalog).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Messages.Count);
        Assert.Contains("https://www.asus.com/support/", result.Messages[1]);
    }

    [Fact]
    public async Task Device_without_hardware_id_still_listed()
    {
        var device = new ProblemDevice("Mystery", "X", 10, []);
        var ctx = Arrange(new FakeHardwareInfo(FakeHardwareInfo.Sample(problemDevices: [device])));

        var result = await new LeftoverReportStep(Catalog).ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        var message = Assert.Single(result.Messages);
        Assert.Contains("Mystery", message);
    }

    [Fact]
    public async Task Reads_hardware_again_at_run_time()
    {
        var hw = new FakeHardwareInfo(FakeHardwareInfo.Sample());
        var ctx = Arrange(hw);
        var step = new LeftoverReportStep(Catalog);

        hw.Snapshot = FakeHardwareInfo.Sample(problemDevices: [new ProblemDevice("Late", "X", 28, ["A"])]);
        var result = await step.ExecuteAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Contains("Late", result.Messages[0]);
    }
}
