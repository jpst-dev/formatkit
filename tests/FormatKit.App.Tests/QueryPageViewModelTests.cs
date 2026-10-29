using FormatKit.App.ViewModels;

namespace FormatKit.App.Tests;

public class QueryPageViewModelTests
{
    private sealed class TestPage : QueryPageViewModel
    {
        public Task RunAsync<T>(Func<CancellationToken, Task<T>> query, Action<T> apply) => RunQueryAsync(query, apply);
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

    [Fact]
    public async Task StaleResult_IsNotApplied_AndBusyStaysUntilTheNewestFinishes()
    {
        var page = new TestPage();
        var firstGate = new TaskCompletionSource<int>();
        var secondGate = new TaskCompletionSource<int>();
        var applied = new List<int>();

        var first = page.RunAsync(_ => firstGate.Task, applied.Add);
        var second = page.RunAsync(_ => secondGate.Task, applied.Add);
        firstGate.SetResult(1);
        await first;

        Assert.Empty(applied);
        Assert.True(page.IsBusy);

        secondGate.SetResult(2);
        await second;

        Assert.Equal([2], applied);
        Assert.False(page.IsBusy);
    }

    [Fact]
    public async Task RefusedQuery_SetsErrorAndHasError()
    {
        var page = new TestPage();

        await page.RunAsync<int>(_ => throw new System.Runtime.InteropServices.COMException("refused"), _ => { });

        Assert.True(page.HasError);
        Assert.Contains("refused", page.Error);
        Assert.False(page.IsBusy);
    }

    [Fact]
    public async Task ApplyThrowing_SetsErrorInsteadOfEscaping()
    {
        var page = new TestPage();

        await page.RunAsync(_ => Task.FromResult(1), _ => throw new InvalidOperationException("boom"));

        Assert.Contains("boom", page.Error);
        Assert.False(page.IsBusy);
    }

    [Fact]
    public async Task QueryBlockingItsThread_DoesNotBlockTheCaller()
    {
        // A synchronous check (the user folders scan) must not run on the UI thread.
        var page = new TestPage();
        using var gate = new ManualResetEventSlim();
        var value = 0;

        var run = page.RunAsync(_ => { gate.Wait(TimeSpan.FromSeconds(5)); return Task.FromResult(3); }, v => value = v);

        Assert.False(run.IsCompleted);
        Assert.True(page.IsBusy);
        gate.Set();
        await run;
        Assert.Equal(3, value);
        Assert.False(page.IsBusy);
    }

    [Fact]
    public async Task BlockingQuery_LeftBeforeItEnds_AppliesNothing()
    {
        var page = new TestPage();
        using var gate = new ManualResetEventSlim();
        var applied = 0;

        var run = page.RunAsync(_ => { gate.Wait(TimeSpan.FromSeconds(5)); return Task.FromResult(1); }, _ => applied++);
        page.Leave();
        gate.Set();
        await run;

        Assert.Equal(0, applied);
        Assert.False(page.IsBusy);
        Assert.False(page.HasError);
    }
}
