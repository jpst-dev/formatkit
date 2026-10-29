namespace FormatKit.App.Tests;

public class SingleInstanceTests
{
    private static string UniqueName() => $"Local\\FormatKit.App.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void TryAcquire_Twice_SecondIsNull()
    {
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name);
        using var second = SingleInstance.TryAcquire(name);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void SignalExisting_RaisesActivated()
    {
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        using var raised = new ManualResetEventSlim();
        first.Activated += (_, _) => raised.Set();

        SingleInstance.SignalExisting(name);

        Assert.True(raised.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SignalExisting_NoOwner_DoesNothing()
    {
        var exception = Record.Exception(() => SingleInstance.SignalExisting(UniqueName()));

        Assert.Null(exception);
    }
}
