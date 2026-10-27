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
}
