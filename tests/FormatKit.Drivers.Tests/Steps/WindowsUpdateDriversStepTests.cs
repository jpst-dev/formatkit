using FormatKit.Core.Steps;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Core.WindowsUpdate;
using FormatKit.Drivers.Steps;

namespace FormatKit.Drivers.Tests.Steps;

public class WindowsUpdateDriversStepTests
{
    private static (WindowsUpdateDriversStep Step, FakeWindowsUpdateClient Wu, StepContext Ctx) Arrange(int updateCount = 2)
    {
        var wu = new FakeWindowsUpdateClient();
        for (var i = 1; i <= updateCount; i++)
        {
            wu.Updates.Add(new DriverUpdate($"id-{i}", $"Driver {i}", $"PCI_VEN_{i}"));
        }

        return (new WindowsUpdateDriversStep(), wu, TestServices.Context(TestServices.Create(wu: wu)));
    }

    [Fact]
    public void Has_expected_metadata()
    {
        var step = new WindowsUpdateDriversStep();

        Assert.Equal("drivers.windows-update", step.Id);
        Assert.True(step.RequiresAdmin);
        Assert.False(step.IsCritical);
        Assert.False(string.IsNullOrWhiteSpace(step.Title));
    }

    [Fact]
    public async Task Check_true_when_no_driver_updates()
    {
        var (step, _, ctx) = Arrange(0);

        Assert.True(await step.CheckAsync(ctx, CancellationToken.None));
    }

    [Fact]
    public async Task Check_false_when_updates_exist()
    {
        var (step, _, ctx) = Arrange();

        Assert.False(await step.CheckAsync(ctx, CancellationToken.None));
    }

    [Fact]
    public async Task Installs_found_updates_and_succeeds()
    {
        var (step, wu, ctx) = Arrange();

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Contains(result.Messages, m => m.Contains("Driver 1"));
        Assert.Contains(result.Messages, m => m.Contains("Driver 2"));
        Assert.Single(wu.Installed);
        Assert.Equal(2, wu.Installed[0].Count);
    }

    [Fact]
    public async Task Reboot_required_returns_RebootRequired()
    {
        var (step, wu, ctx) = Arrange();
        wu.InstallResult = new WindowsUpdateInstallResult(true, true, 0, []);

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.Equal(StepOutcome.RebootRequired, result.Outcome);
    }

    [Fact]
    public async Task Failure_uses_described_hresult_as_next_action()
    {
        var (step, wu, ctx) = Arrange();
        var hr = unchecked((int)0x8024402C);
        wu.InstallResult = new WindowsUpdateInstallResult(false, false, hr, ["Driver 2"]);

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(WindowsUpdateErrors.Describe(hr), result.NextAction);
        Assert.Contains(result.Messages, m => m.Contains("Driver 2"));
    }
}
