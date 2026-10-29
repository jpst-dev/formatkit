using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using FormatKit.App.Worker;
using Serilog;

namespace FormatKit.App.Tests;

public class WorkerPipeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int Me => Environment.ProcessId;

    private static string NewName() => "FormatKit-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Accept_NoClientWithinTimeout_ReturnsNull()
    {
        var accepted = await WorkerPipe.AcceptAsync(NewName(), Me, TimeSpan.FromMilliseconds(100), Ct);

        Assert.Null(accepted);
    }

    [Fact]
    public async Task Accept_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => WorkerPipe.AcceptAsync(NewName(), Me, TimeSpan.FromSeconds(30), cts.Token));
    }

    [Fact]
    public async Task Pipe_ConnectAndExchangeOneMessage()
    {
        var name = NewName();
        var log = new LoggerConfiguration().CreateLogger();
        var accepting = WorkerPipe.AcceptAsync(name, Me, TimeSpan.FromSeconds(10), Ct);

        // Both ends run unelevated here; the client also checks that the server's owner is the current user.
        await using var window = new MessageChannel(await WorkerPipe.ConnectAsync(name, TimeSpan.FromSeconds(10), Ct), log);
        var serverStream = await accepting;
        Assert.NotNull(serverStream);
        await using var worker = new MessageChannel(serverStream, log);

        await worker.SendAsync(new Failed("boom"), Ct);
        await window.SendAsync(new Cancel(), Ct);

        await using var toWindow = window.ReadAllAsync(Ct).GetAsyncEnumerator(Ct);
        Assert.True(await toWindow.MoveNextAsync());
        Assert.Equal("boom", Assert.IsType<Failed>(toWindow.Current).Message);
        await using var toWorker = worker.ReadAllAsync(Ct).GetAsyncEnumerator(Ct);
        Assert.True(await toWorker.MoveNextAsync());
        Assert.IsType<Cancel>(toWorker.Current);
    }

    [Fact]
    public async Task Accept_OnlyOneConnection()
    {
        var name = NewName();
        var accepting = WorkerPipe.AcceptAsync(name, Me, TimeSpan.FromSeconds(10), Ct);
        await using var first = await WorkerPipe.ConnectAsync(name, TimeSpan.FromSeconds(10), Ct);
        await using var server = await accepting;
        Assert.NotNull(server);

        await Assert.ThrowsAsync<TimeoutException>(() => WorkerPipe.ConnectAsync(name, TimeSpan.FromMilliseconds(200), Ct));
    }

    [Fact]
    public async Task Accept_NameAlreadyTaken_Throws()
    {
        var name = NewName();
        await using var squatter = new NamedPipeServerStream(
            name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        await Assert.ThrowsAnyAsync<Exception>(() => WorkerPipe.AcceptAsync(name, Me, TimeSpan.FromSeconds(5), Ct));

        // The worker did not join the squatter's pipe: a client still reaches the squatter.
        var squatterAccepts = squatter.WaitForConnectionAsync(Ct);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000, Ct);
        await squatterAccepts.WaitAsync(TimeSpan.FromSeconds(5), Ct);
    }

    [Fact]
    public async Task Accept_WrongClientProcess_ThrowsAndClosesThePipe()
    {
        var name = NewName();
        var accepting = WorkerPipe.AcceptAsync(name, Me + 4, TimeSpan.FromSeconds(10), Ct);
        await using var client = await WorkerPipe.ConnectAsync(name, TimeSpan.FromSeconds(10), Ct);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => accepting);

        var buffer = new byte[1];
        Assert.Equal(0, await client.ReadAsync(buffer, Ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), Ct));
    }

    [Fact]
    public async Task Connect_NoServer_ThrowsTimeout()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => WorkerPipe.ConnectAsync(NewName(), TimeSpan.FromMilliseconds(100), Ct));
    }

    [Fact]
    public void Security_GrantsOnlyTheCurrentUserAndMakesItOwner()
    {
        var user = WindowsIdentity.GetCurrent().User!;

        var security = WorkerPipe.CreateSecurity();

        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
        var rule = Assert.Single(rules);
        Assert.Equal(user, rule.IdentityReference);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.Equal(PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, rule.PipeAccessRights);
        Assert.Equal(user, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.True(security.AreAccessRulesProtected);
    }
}
