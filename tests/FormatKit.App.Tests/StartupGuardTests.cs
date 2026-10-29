namespace FormatKit.App.Tests;

public sealed class StartupGuardTests
{
    [Fact]
    public void StartupThrowing_ReportsAndShutsDownWithOne()
    {
        Exception? reported = null;
        int? code = null;

        var ok = StartupGuard.Run(() => throw new InvalidOperationException("xaml"), ex => reported = ex, c => code = c);

        Assert.False(ok);
        Assert.Equal("xaml", reported?.Message);
        Assert.Equal(1, code);
    }

    [Fact]
    public void StartupSucceeding_NeitherReportsNorShutsDown()
    {
        var reports = 0;
        int? code = null;

        var ok = StartupGuard.Run(() => { }, _ => reports++, c => code = c);

        Assert.True(ok);
        Assert.Equal(0, reports);
        Assert.Null(code);
    }

    [Fact]
    public void ReportThrowing_StillShutsDown()
    {
        int? code = null;

        StartupGuard.Run(() => throw new InvalidOperationException("a"), _ => throw new IOException("b"), c => code = c);

        Assert.Equal(1, code);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void UnhandledException_ShutsDownOnlyWithoutALoadedWindow(bool windowLoaded, bool shutsDown)
    {
        Assert.Equal(shutsDown, StartupGuard.ShutsDownAfterUnhandled(windowLoaded));
    }
}
