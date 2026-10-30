using System.Security.AccessControl;
using System.Security.Principal;

namespace FormatKit.App;

/// <summary>
/// Keeps one window per user session: the first instance owns a named mutex and listens on two named events that
/// later instances signal before they exit: one to come to the front, one to also resume the interrupted run (a
/// <c>--resume</c> start by the resume task after a reboot). All three objects grant only the current user, so an
/// elevated owner (the resume task) and an unelevated later start still see each other: the elevated default DACL
/// would admit only Administrators and SYSTEM.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly EventWaitHandle _resume;
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _listener;

    private SingleInstance(Mutex mutex, EventWaitHandle activate, EventWaitHandle resume)
    {
        _mutex = mutex;
        _activate = activate;
        _resume = resume;
        _listener = new Thread(Listen) { IsBackground = true, Name = "FormatKit.SingleInstance" };
    }

    /// <summary>Raised on a background thread when a later instance asks the first one to come forward.</summary>
    public event EventHandler? Activated;

    /// <summary>
    /// Raised on a background thread when a later <c>--resume</c> start asks the first one to come forward and resume
    /// the interrupted run (instead of <see cref="Activated"/>).
    /// </summary>
    public event EventHandler? ResumeRequested;

    /// <summary>
    /// Returns the owner, or null when another instance already owns <paramref name="name"/>, including one whose
    /// objects this process may not open.
    /// </summary>
    public static SingleInstance? TryAcquire(string name)
    {
        var user = CurrentUser();
        EventWaitHandle? activate = null;
        EventWaitHandle? resume = null;
        try
        {
            // The events exist before the mutex is taken, so a second start can always signal an owner.
            activate = EventWaitHandleAcl.Create(
                false, EventResetMode.AutoReset, ActivateName(name), out _, EventSecurity(user));
            resume = EventWaitHandleAcl.Create(
                false, EventResetMode.AutoReset, ResumeName(name), out _, EventSecurity(user));
            var mutex = MutexAcl.Create(initiallyOwned: true, name, out var createdNew, MutexSecurity(user));
            if (!createdNew)
            {
                mutex.Dispose();
                resume.Dispose();
                activate.Dispose();
                return null;
            }

            return new SingleInstance(mutex, activate, resume);
        }
        catch (UnauthorizedAccessException)
        {
            resume?.Dispose();
            activate?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Asks the instance that owns <paramref name="name"/> to come to the front, and with <paramref name="resume"/> to
    /// resume the interrupted run too, if there is one. An owner without the resume event (an older build) is only
    /// asked to come to the front.
    /// </summary>
    public static void SignalExisting(string name, bool resume)
    {
        try
        {
            if ((resume && EventWaitHandle.TryOpenExisting(ResumeName(name), out var handle))
                || EventWaitHandle.TryOpenExisting(ActivateName(name), out handle))
            {
                using (handle)
                    handle.Set();
            }
        }
        catch (UnauthorizedAccessException)
        {
            // An owner this process may not signal: it stays where it is, and this start still exits quietly.
        }
    }

    /// <summary>
    /// Starts raising <see cref="Activated"/> and <see cref="ResumeRequested"/>; call it once the handlers are
    /// subscribed. A signal sent earlier stays on its event and is raised then, so a resume request sent while this
    /// window is still starting is not lost.
    /// </summary>
    public void Start() => _listener.Start();

    public void Dispose()
    {
        _stop.Set();
        if (_listener.IsAlive)
            _listener.Join();
        _stop.Dispose();
        _resume.Dispose();
        _activate.Dispose();
        // Disposing the handle ends the ownership; ReleaseMutex would throw when called from another thread.
        _mutex.Dispose();
    }

    private static string ActivateName(string name) => name + ".Activate";

    private static string ResumeName(string name) => name + ".Resume";

    private static SecurityIdentifier CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw new InvalidOperationException("The current token has no user SID.");
    }

    private static EventWaitHandleSecurity EventSecurity(SecurityIdentifier user)
    {
        var security = new EventWaitHandleSecurity();
        security.SetOwner(user);
        security.AddAccessRule(new EventWaitHandleAccessRule(
            user, EventWaitHandleRights.FullControl, AccessControlType.Allow));
        return security;
    }

    private static MutexSecurity MutexSecurity(SecurityIdentifier user)
    {
        var security = new MutexSecurity();
        security.SetOwner(user);
        security.AddAccessRule(new MutexAccessRule(user, MutexRights.FullControl, AccessControlType.Allow));
        return security;
    }

    private void Listen()
    {
        WaitHandle[] handles = [_activate, _resume, _stop];
        while (true)
        {
            switch (WaitHandle.WaitAny(handles))
            {
                case 0:
                    Activated?.Invoke(this, EventArgs.Empty);
                    break;
                case 1:
                    ResumeRequested?.Invoke(this, EventArgs.Empty);
                    break;
                default:
                    return;
            }
        }
    }
}
