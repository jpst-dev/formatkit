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

    [Fact]
    public async Task Disable_target_on_absent_feature_is_already_applied_but_enable_capture_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var ctx = TestServices.Context(TestServices.Create());
        var handler = new OptionalFeatureHandler();

        Assert.True(await handler.IsAppliedAsync(new OptionalFeatureAction("Missing", OptionalFeatureState.Disabled), ctx, ct));
        var enable = new OptionalFeatureAction("Missing", OptionalFeatureState.Enabled);
        Assert.False(await handler.IsAppliedAsync(enable, ctx, ct));
        await Assert.ThrowsAsync<TargetNotPresentException>(() => handler.CaptureAsync(enable, ctx, ct));
    }
}
