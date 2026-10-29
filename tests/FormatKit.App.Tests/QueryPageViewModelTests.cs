using FormatKit.App.ViewModels;

namespace FormatKit.App.Tests;

public class QueryPageViewModelTests
{
    private sealed class TestPage : QueryPageViewModel
    {
        public Task RunAsync<T>(Func<CancellationToken, Task<T>> query, Action<T> apply) => RunQueryAsync(query, apply);
    }

    [Fact]
    public async Task Leave_CancelsRunningQuery()
    {
        var page = new TestPage();
        var started = new TaskCompletionSource();
        CancellationToken seen = default;
        var applied = false;

        var run = page.RunAsync<int>(async ct =>
        {
            seen = ct;
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return 1;
        }, _ => applied = true);
        await started.Task;
        Assert.True(page.IsBusy);

        page.Leave();
        await run;

        Assert.True(seen.IsCancellationRequested);
        Assert.False(page.IsBusy);
        Assert.Null(page.Error);
        Assert.False(applied);
    }

    [Fact]
    public async Task RunQuery_AppliesValueAndClearsBusy()
    {
        var page = new TestPage();
        var value = 0;

        await page.RunAsync(_ => Task.FromResult(7), v => value = v);

        Assert.Equal(7, value);
        Assert.False(page.IsBusy);
        Assert.False(page.HasError);
    }
}
