using FormatKit.Core.WindowsUpdate;

namespace FormatKit.Core.Tests.WindowsUpdate;

public class ComWindowsUpdateClientIntegrationTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task SearchDrivers_runs_on_real_machine()
    {
        var updates = await new ComWindowsUpdateClient().SearchDriversAsync(CancellationToken.None);

        Assert.NotNull(updates);
    }
}
