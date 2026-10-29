using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace FormatKit.App.Worker;

/// <summary>
/// The named pipe between the window (client) and the worker (server). The server allows only the current user's SID
/// and one instance; both ends are asynchronous, so a pending read ends when it is cancelled.
/// </summary>
public static class WorkerPipe
{
    /// <summary>How long the worker waits for the window to connect.</summary>
    public static readonly TimeSpan AcceptTimeout = TimeSpan.FromSeconds(30);

    // A zero-size pipe buffer makes every write wait for the other end to read it, so both ends sending would deadlock.
    private const int BufferSize = 64 * 1024;

    /// <summary>
    /// Creates the server and waits for one client, which must be the process <paramref name="clientProcessId"/> (the
    /// window that started the worker). Returns null when none connects within <paramref name="timeout"/>. Throws when
    /// <paramref name="ct"/> is cancelled, when the name is already taken, and <see cref="UnauthorizedAccessException"/>
    /// (after closing the pipe) when another process connected.
    /// </summary>
    public static async Task<Stream?> AcceptAsync(string name, int clientProcessId, TimeSpan timeout, CancellationToken ct)
    {
        // maxNumberOfServerInstances 1 also makes the server the first instance, so a squatted name fails here.
        var server = NamedPipeServerStreamAcl.Create(
            name,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: BufferSize,
            outBufferSize: BufferSize,
            CreateSecurity());
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            await server.WaitForConnectionAsync(timeoutCts.Token).ConfigureAwait(false);
            if (!NativeMethods.GetNamedPipeClientProcessId(server.SafePipeHandle, out var connected))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (connected != clientProcessId)
                throw new UnauthorizedAccessException($"Process {connected} connected to {name}; expected {clientProcessId}.");
            return server;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await server.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Connects to the worker's server. Throws <see cref="TimeoutException"/> when none accepts within
    /// <paramref name="timeout"/>, and <see cref="UnauthorizedAccessException"/> when the pipe is not owned by the
    /// current user.
    /// </summary>
    public static async Task<Stream> ConnectAsync(string name, TimeSpan timeout, CancellationToken ct)
    {
        // Not PipeOptions.CurrentUserOnly: its owner check compares with the token's default owner, the Administrators
        // group in an elevated window (after a resume), so it would refuse the worker's pipe, owned by the user SID.
        // Identification: the worker may identify the window's user but never act as it.
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            await client.ConnectAsync(timeout, ct).ConfigureAwait(false);
            var owner = client.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
            if (owner is null || owner != CurrentUser())
                throw new UnauthorizedAccessException($"The pipe {name} is not owned by the current user.");
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Read and write for the current user's SID only, and that SID as owner. Not <c>PipeOptions.CurrentUserOnly</c>
    /// on the server: it uses the token's default owner, which in an elevated process is the Administrators group,
    /// whose membership an unelevated window does not use; the user SID is the same in both.
    /// </summary>
    internal static PipeSecurity CreateSecurity()
    {
        var user = CurrentUser();
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        security.SetOwner(user);
        return security;
    }

    /// <summary>The user SID of this process's token: the same elevated or not.</summary>
    private static SecurityIdentifier CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw new InvalidOperationException("The current user has no SID.");
    }
}
