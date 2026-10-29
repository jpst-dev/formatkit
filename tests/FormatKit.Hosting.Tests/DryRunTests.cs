using FormatKit.Core.Tests.Fakes;
using FormatKit.Hosting;

namespace FormatKit.Hosting.Tests;

public class DryRunTests
{
    [Fact]
    public void Items_OfAStepWithoutItemsOrPrograms_IsEmpty()
    {
        Assert.Empty(DryRun.Items(new FakeStep("drivers.scan")));
    }
}
