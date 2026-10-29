namespace FormatKit.App.Tests;

public sealed class CloseGuardTests
{
    [Fact]
    public void NotRunning_ClosesWithoutAsking()
    {
        var asked = 0;
        var guard = new CloseGuard(() => false, () => { asked++; return Task.FromResult(true); });

        Assert.False(guard.ShouldCancel(() => { }));
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task Running_CancelsThenClosesWhenConfirmed_WithoutAskingAgain()
    {
        var asked = 0;
        var answer = new TaskCompletionSource<bool>();
        var guard = new CloseGuard(() => true, () => { asked++; return answer.Task; });
        var closes = 0;

        Assert.True(guard.ShouldCancel(() => closes++));
        Assert.True(guard.ShouldCancel(() => closes++)); // a second request while asking: no second dialog
        Assert.Equal(1, asked);
        Assert.Equal(0, closes);

        answer.SetResult(true);
        await guard.Asking;

        Assert.Equal(1, closes);
        Assert.False(guard.ShouldCancel(() => closes++)); // the final close goes through
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task Running_Declined_StaysOpenAndAsksNextTime()
    {
        var asked = 0;
        var guard = new CloseGuard(() => true, () => { asked++; return Task.FromResult(false); });
        var closes = 0;

        Assert.True(guard.ShouldCancel(() => closes++));
        await guard.Asking;
        Assert.True(guard.ShouldCancel(() => closes++));
        await guard.Asking;

        Assert.Equal(2, asked);
        Assert.Equal(0, closes);
    }
}
