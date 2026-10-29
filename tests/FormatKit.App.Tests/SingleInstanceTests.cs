using System.Security.AccessControl;
using System.Security.Principal;

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

        SingleInstance.SignalExisting(name, resume: false);

        Assert.True(raised.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SignalExisting_Resume_RaisesResumeRequestedOnly()
    {
        // The resume task after a reboot found a window open: that window must resume the run, not only come forward.
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        using var resumed = new ManualResetEventSlim();
        var activated = false;
        first.ResumeRequested += (_, _) => resumed.Set();
        first.Activated += (_, _) => activated = true;

        SingleInstance.SignalExisting(name, resume: true);

        Assert.True(resumed.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.False(activated);
    }

    [Fact]
    public void SignalExisting_WithoutResume_DoesNotRaiseResumeRequested()
    {
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        using var activated = new ManualResetEventSlim();
        var resumed = false;
        first.ResumeRequested += (_, _) => resumed = true;
        first.Activated += (_, _) => activated.Set();

        SingleInstance.SignalExisting(name, resume: false);

        Assert.True(activated.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.False(resumed);
    }

    [Fact]
    public void SignalExisting_Resume_OwnerWithoutResumeEvent_StillActivates()
    {
        // An owner started by an older build listens only on the activate event: it at least comes forward.
        var name = UniqueName();
        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");

        SingleInstance.SignalExisting(name, resume: true);

        Assert.True(activate.WaitOne(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void SignalExisting_NoOwner_DoesNothing()
    {
        var exception = Record.Exception(() => SingleInstance.SignalExisting(UniqueName(), resume: false));

        Assert.Null(exception);
    }

    private static SecurityIdentifier CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!;
    }

    private static bool GrantsOnlyCurrentUser(AuthorizationRuleCollection rules, int rights)
    {
        // Only the user: the default DACL of an elevated token lists Administrators and SYSTEM instead.
        var user = CurrentUser();
        var all = rules.OfType<AccessRule>().ToList();
        return all.Count > 0 && all.All(r => user.Equals(r.IdentityReference)) && all.Any(r =>
            r.AccessControlType == AccessControlType.Allow
            && user.Equals(r.IdentityReference)
            && (r switch
            {
                MutexAccessRule m => (int)m.MutexRights,
                EventWaitHandleAccessRule ev => (int)ev.EventWaitHandleRights,
                _ => 0,
            } & rights) == rights);
    }

    [Fact]
    public void TryAcquire_GrantsTheCurrentUserOnTheMutexAndTheEvent()
    {
        // An elevated owner would otherwise get the admin-only default DACL and lock the unelevated user out.
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);

        using var mutex = MutexAcl.OpenExisting(name, MutexRights.ReadPermissions);
        using var activate = EventWaitHandleAcl.OpenExisting(name + ".Activate", EventWaitHandleRights.ReadPermissions);
        using var resume = EventWaitHandleAcl.OpenExisting(name + ".Resume", EventWaitHandleRights.ReadPermissions);

        Assert.True(GrantsOnlyCurrentUser(
            mutex.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)),
            (int)(MutexRights.Synchronize | MutexRights.Modify)));
        Assert.True(GrantsOnlyCurrentUser(
            activate.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)),
            (int)(EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify)));
        Assert.True(GrantsOnlyCurrentUser(
            resume.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)),
            (int)(EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify)));
    }

    [Fact]
    public void TryAcquire_AccessDenied_TreatsItAsAnotherInstance()
    {
        // Stands in for an owner whose objects this process may not fully open (another elevation level).
        var name = UniqueName();
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(
            CurrentUser(), EventWaitHandleRights.ReadPermissions, AccessControlType.Allow));
        using var foreign = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name + ".Activate", out _, security);

        SingleInstance? second = null;
        var exception = Record.Exception(() => second = SingleInstance.TryAcquire(name));

        Assert.Null(exception);
        Assert.Null(second);
    }

    [Fact]
    public void TryAcquire_ResumeEventAccessDenied_TreatsItAsAnotherInstance()
    {
        var name = UniqueName();
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(
            CurrentUser(), EventWaitHandleRights.ReadPermissions, AccessControlType.Allow));
        using var foreign = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name + ".Resume", out _, security);

        SingleInstance? second = null;
        var exception = Record.Exception(() => second = SingleInstance.TryAcquire(name));

        Assert.Null(exception);
        Assert.Null(second);
        // Nothing was left held: once the foreign event is gone a later start becomes the owner.
        foreign.Dispose();
        using var later = SingleInstance.TryAcquire(name);
        Assert.NotNull(later);
    }

    [Fact]
    public void SignalExisting_ResumeAccessDenied_DoesNotThrow()
    {
        var name = UniqueName();
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(
            CurrentUser(), EventWaitHandleRights.ReadPermissions, AccessControlType.Allow));
        using var foreign = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name + ".Resume", out _, security);

        var exception = Record.Exception(() => SingleInstance.SignalExisting(name, resume: true));

        Assert.Null(exception);
    }

    [Fact]
    public void SignalExisting_AccessDenied_DoesNotThrow()
    {
        var name = UniqueName();
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(
            CurrentUser(), EventWaitHandleRights.ReadPermissions, AccessControlType.Allow));
        using var foreign = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name + ".Activate", out _, security);

        var exception = Record.Exception(() => SingleInstance.SignalExisting(name, resume: false));

        Assert.Null(exception);
    }
}
