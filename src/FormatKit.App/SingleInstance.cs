namespace FormatKit.App;

/// <summary>
/// Keeps one window per user session: the first instance owns a named mutex and listens on a named event that
/// later instances signal before they exit.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _listener;

    private SingleInstance(Mutex mutex, EventWaitHandle activate)
    {
        _mutex = mutex;
        _activate = activate;
        _listener = new Thread(Listen) { IsBackground = true, Name = "FormatKit.SingleInstance" };
        _listener.Start();
    }

    /// <summary>Raised on a background thread when a later instance asks the first one to come forward.</summary>
    public event EventHandler? Activated;

    /// <summary>Returns the owner, or null when another instance already owns <paramref name="name"/>.</summary>
    public static SingleInstance? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName(name)));
    }

    /// <summary>Asks the instance that owns <paramref name="name"/> to come to the front, if there is one.</summary>
    public static void SignalExisting(string name)
    {
        if (EventWaitHandle.TryOpenExisting(ActivateName(name), out var handle))
        {
            using (handle)
                handle.Set();
        }
    }

    public void Dispose()
    {
        _stop.Set();
        _listener.Join();
        _stop.Dispose();
        _activate.Dispose();
        // Disposing the handle ends the ownership; ReleaseMutex would throw when called from another thread.
        _mutex.Dispose();
    }

    private static string ActivateName(string name) => name + ".Activate";

    private void Listen()
    {
        WaitHandle[] handles = [_activate, _stop];
        while (WaitHandle.WaitAny(handles) == 0)
            Activated?.Invoke(this, EventArgs.Empty);
    }
}
