namespace FormatKit.App;

/// <summary>
/// Decides the window's close while a run may be going on: during a run the close is cancelled and
/// <c>confirm</c> is asked once (a second close request while it is asking is cancelled without a second dialog);
/// when it answers yes, <c>close</c> runs and that final close goes through without asking again.
/// </summary>
public sealed class CloseGuard(Func<bool> isRunning, Func<Task<bool>> confirm)
{
    private bool _confirmed;
    private bool _asking;

    /// <summary>The pending question, for tests; completed when none is pending.</summary>
    public Task Asking { get; private set; } = Task.CompletedTask;

    /// <summary>True when the window must cancel this close.</summary>
    public bool ShouldCancel(Action close)
    {
        if (_confirmed) return false;
        if (_asking) return true;
        if (!isRunning()) return false;

        _asking = true;
        Asking = AskAsync(close);
        return true;
    }

    private async Task AskAsync(Action close)
    {
        bool yes;
        try
        {
            yes = await confirm();
        }
        finally
        {
            _asking = false;
        }

        if (!yes) return;
        _confirmed = true;
        close();
    }
}
