using System.Globalization;
using FormatKit.Core.Running;

namespace FormatKit.Core.Tests.Running;

[Collection("Culture")]
public sealed class StartCountdownTests : IDisposable
{
    private readonly CultureInfo _original = CultureInfo.CurrentUICulture;
    private readonly List<string> _lines = [];

    public StartCountdownTests() => CultureInfo.CurrentUICulture = new CultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentUICulture = _original;

    [Fact]
    public async Task Without_a_key_it_waits_the_whole_countdown_and_proceeds()
    {
        var delays = new List<TimeSpan>();
        var countdown = new StartCountdown(() => false, (d, _) => { delays.Add(d); return Task.CompletedTask; }, _lines.Add);

        var proceed = await countdown.WaitAsync("Home PC", TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        Assert.True(proceed);
        Assert.Equal("Starting the profile 'Home PC' in 60 s (press Esc to cancel)...", _lines[0]);
        Assert.Equal(6, _lines.Count); // printed at 60, 50, 40, 30, 20 and 10 s
        Assert.Contains("10 s", _lines[^1]);
        Assert.Equal(TimeSpan.FromSeconds(60), TimeSpan.FromTicks(delays.Sum(d => d.Ticks)));
    }

    [Fact]
    public async Task Esc_on_the_third_poll_cancels()
    {
        var polls = 0;
        var countdown = new StartCountdown(() => ++polls == 3, (_, _) => Task.CompletedTask, _lines.Add);

        var proceed = await countdown.WaitAsync("Home PC", TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        Assert.False(proceed);
        Assert.Equal(3, polls);
        Assert.Equal(2, _lines.Count);
        Assert.StartsWith("Starting the profile 'Home PC' in 60 s", _lines[0]);
        Assert.Equal("Automatic start cancelled.", _lines[1]);
    }

    [Fact]
    public async Task A_zero_countdown_proceeds_without_output()
    {
        var countdown = new StartCountdown(() => true, (_, _) => Task.CompletedTask, _lines.Add);

        Assert.True(await countdown.WaitAsync("p", TimeSpan.Zero, TestContext.Current.CancellationToken));
        Assert.Empty(_lines);
    }

    [Fact]
    public async Task The_cancellation_token_stops_the_wait()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var countdown = new StartCountdown(() => false, (_, _) => Task.CompletedTask, _lines.Add);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => countdown.WaitAsync("p", TimeSpan.FromSeconds(5), cts.Token));
    }
}
