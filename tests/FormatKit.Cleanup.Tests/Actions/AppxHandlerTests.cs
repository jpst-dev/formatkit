using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Localization;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Cleanup.Tests.Actions;

public class AppxHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly AppxAction Action = new("Microsoft.BingNews", "9WZDNCRFHVFW");

    private static ProcessResult Result(int exit) => new(exit, "out", "", TimeSpan.Zero, false);

    [Fact]
    public async Task Applied_when_absent_everywhere()
    {
        var appx = new FakeAppxPackages();
        var ctx = TestServices.Context(TestServices.Create(appx: appx));
        var handler = new AppxHandler();

        Assert.True(await handler.IsAppliedAsync(Action, ctx, Ct));

        appx.States["Microsoft.BingNews"] = new AppxState(false, true);
        Assert.False(await handler.IsAppliedAsync(Action, ctx, Ct));
    }

    [Fact]
    public async Task Apply_removes_and_verifies()
    {
        var appx = new FakeAppxPackages();
        appx.States["Microsoft.BingNews"] = new AppxState(true, true);
        var ctx = TestServices.Context(TestServices.Create(appx: appx));
        var handler = new AppxHandler();

        var captured = await handler.CaptureAsync(Action, ctx, Ct);
        Assert.True(captured.Previous.GetProperty("provisioned").GetBoolean());
        Assert.Equal("9WZDNCRFHVFW", captured.Previous.GetProperty("storeId").GetString());

        await handler.ApplyAsync(Action, captured, ctx, Ct);

        Assert.Equal(["Microsoft.BingNews"], appx.Removed);
    }

    [Fact]
    public async Task Undo_installs_store_id_from_msstore()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue("winget.exe", Result(0));
        var ctx = TestServices.Context(TestServices.Create(processes: processes));
        var handler = new AppxHandler();
        var captured = await handler.CaptureAsync(Action, ctx, Ct);

        await handler.UndoAsync(captured, ctx, Ct);

        var call = Assert.Single(processes.Calls);
        Assert.Contains("9WZDNCRFHVFW", call.Arguments);
        var args = call.Arguments.ToList();
        Assert.Equal("msstore", args[args.IndexOf("--source") + 1]);
    }

    [Fact]
    public async Task Undo_winget_missing_throws_with_key()
    {
        var processes = new FakeProcessRunner();
        processes.ThrowNotFound("winget.exe");
        var ctx = TestServices.Context(TestServices.Create(processes: processes));
        var handler = new AppxHandler();
        var captured = await handler.CaptureAsync(Action, ctx, Ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.UndoAsync(captured, ctx, Ct));

        Assert.Equal(Strings.Format("Undo_WingetMissing", "9WZDNCRFHVFW"), ex.Message);
        Assert.NotEqual("Undo_WingetMissing", ex.Message);
    }

    [Fact]
    public async Task Undo_failure_throws_with_store_link()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue("winget.exe", Result(1));
        var ctx = TestServices.Context(TestServices.Create(processes: processes));
        var handler = new AppxHandler();
        var captured = await handler.CaptureAsync(Action, ctx, Ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.UndoAsync(captured, ctx, Ct));

        Assert.Contains("ms-windows-store://pdp/?ProductId=9WZDNCRFHVFW", ex.Message);
    }
}
