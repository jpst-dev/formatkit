using FormatKit.Core.Tests.Fakes;
using Serilog;

namespace FormatKit.Hosting.Tests;

public sealed class ModuleSetTests
{
    [Fact]
    public void CreateReal_ModuleOrder_IsDriversCleanupProgramsDev()
    {
        var set = ModuleSet.CreateReal(
            TestServices.Create(), new LoggerConfiguration().CreateLogger(), TimeSpan.Zero, dev: null);

        Assert.Equal(["drivers", "cleanup", "programs", "dev"], set.Modules.Select(m => m.Name));
        Assert.NotEmpty(set.Vendors.Gpu);
        Assert.NotEmpty(set.Cleanup.Items);
        Assert.NotEmpty(set.Programs.Programs);
    }
}
