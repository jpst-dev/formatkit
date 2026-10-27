using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Cleanup.Tests.Actions;

public class OptionalFeatureHandlerTests
{
    [Fact]
    public async Task Capture_and_undo_restore_state()
    {
        var ct = TestContext.Current.CancellationToken;
        var features = new FakeOptionalFeatures();
        features.States["Recall"] = OptionalFeatureState.Enabled;
        var ctx = TestServices.Context(TestServices.Create(features: features));
        var handler = new OptionalFeatureHandler();
        var action = new OptionalFeatureAction("Recall", OptionalFeatureState.Disabled);

        Assert.False(await handler.IsAppliedAsync(action, ctx, ct));
        var captured = await handler.CaptureAsync(action, ctx, ct);
        Assert.Equal("enabled", captured.Previous.GetProperty("state").GetString());

        var recorded = await handler.ApplyAsync(action, captured, ctx, ct);
        Assert.Equal(OptionalFeatureState.Disabled, features.States["Recall"]);
        Assert.True(await handler.IsAppliedAsync(action, ctx, ct));

        await handler.UndoAsync(recorded, ctx, ct);
        Assert.Equal(OptionalFeatureState.Enabled, features.States["Recall"]);
    }
}
