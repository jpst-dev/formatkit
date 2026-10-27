using FormatKit.Core.Hardware;

namespace FormatKit.Core.Tests.Hardware;

public class WmiHardwareInfoIntegrationTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task Reads_real_machine()
    {
        var snapshot = await new WmiHardwareInfo().ReadAsync(CancellationToken.None);

        Assert.NotNull(snapshot.Machine.Manufacturer);
        Assert.NotNull(snapshot.NetworkAdapters);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ReadDisabledDevices_returns_a_list()
    {
        var devices = await new WmiHardwareInfo().ReadDisabledDevicesAsync(CancellationToken.None);

        Assert.NotNull(devices);
        Assert.All(devices, d => Assert.Equal(22, d.ErrorCode));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task HasBattery_does_not_throw()
    {
        var exception = await Record.ExceptionAsync(() => new WmiHardwareInfo().HasBatteryAsync(CancellationToken.None));

        Assert.Null(exception);
    }
}
