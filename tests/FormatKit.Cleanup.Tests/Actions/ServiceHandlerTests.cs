using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Cleanup.Tests.Actions;

public class ServiceHandlerTests
{
    [Fact]
    public async Task Capture_and_undo_restore_start_type()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new FakeServiceControl();
        services.Services["DiagTrack"] = ServiceStartType.Manual;
        var ctx = TestServices.Context(TestServices.Create(services: services));
        var handler = new ServiceHandler();
        var action = new ServiceAction("DiagTrack", ServiceStartType.Disabled);

        Assert.False(await handler.IsAppliedAsync(action, ctx, ct));
        var captured = await handler.CaptureAsync(action, ctx, ct);
        Assert.Equal("manual", captured.Previous.GetProperty("startType").GetString());

        var recorded = await handler.ApplyAsync(action, captured, ctx, ct);
        Assert.Equal(ServiceStartType.Disabled, services.Services["DiagTrack"]);
        Assert.True(await handler.IsAppliedAsync(action, ctx, ct));

        await handler.UndoAsync(recorded, ctx, ct);
        Assert.Equal(ServiceStartType.Manual, services.Services["DiagTrack"]);
    }
}
