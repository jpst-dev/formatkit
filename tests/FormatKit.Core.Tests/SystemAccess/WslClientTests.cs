using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class WslClientTests
{
    private static ProcessResult Result(int exit = 0, string stdout = "", string stderr = "") =>
        new(exit, stdout, stderr, TimeSpan.Zero, false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_call_sets_WSL_UTF8()
    {
        var runner = new FakeProcessRunner();
        for (var i = 0; i < 7; i++)
        {
            runner.Enqueue("wsl.exe", Result());
        }

        var wsl = new WslClient(runner);
        await wsl.IsAvailableAsync(Ct);
        await wsl.InstallPlatformAsync(Ct);
        await wsl.ListDistrosAsync(Ct);
        await wsl.InstallDistroAsync("Ubuntu", Ct);
        await wsl.ExecAsync("Ubuntu", "root", ["id"], Ct);
        await wsl.WriteFileAsync("Ubuntu", "root", "/etc/x", "y", Ct);
        await wsl.TerminateAsync("Ubuntu", Ct);

        Assert.Equal(7, runner.Calls.Count);
        Assert.All(runner.Calls, c => Assert.Equal("1", c.Environment!["WSL_UTF8"]));
    }

    [Fact]
    public async Task ListDistros_strips_nulls_and_bom()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("wsl.exe", Result(stdout: "﻿Ubuntu\0\r\nDebian\r\n\r\n"));

        var distros = await new WslClient(runner).ListDistrosAsync(Ct);

        Assert.Equal(["Ubuntu", "Debian"], distros);
    }

    [Fact]
    public async Task ListDistros_is_empty_on_failure()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("wsl.exe", Result(exit: 1, stdout: "Ubuntu\r\n"));

        Assert.Empty(await new WslClient(runner).ListDistrosAsync(Ct));
    }

    [Fact]
    public async Task Exec_passes_command_as_separate_arguments()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("wsl.exe", Result());

        await new WslClient(runner).ExecAsync("Ubuntu", "root", ["id", "-u", "joao"], Ct);

        var call = Assert.Single(runner.Calls);
        Assert.Equal(["-d", "Ubuntu", "-u", "root", "--exec", "id", "-u", "joao"], call.Arguments);
        Assert.Equal(Timeouts.Query, call.Timeout);
    }

    [Fact]
    public async Task WriteFile_uses_constant_script()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("wsl.exe", Result());

        await new WslClient(runner).WriteFileAsync("Ubuntu", "root", "/etc/wsl.conf", "[user]\ndefault=joao\n", Ct);

        var args = Assert.Single(runner.Calls).Arguments;
        Assert.Equal(
            ["sh", "-c", WslClient.WriteScript, "sh", "/etc/wsl.conf", "[user]\ndefault=joao\n"],
            args.Skip(args.Count - 6));
    }

    [Fact]
    public void IsVirtualizationError_detects_both_tokens()
    {
        Assert.True(WslClient.IsVirtualizationError(Result(1, stderr: "Error: HCS_E_HYPERV_NOT_INSTALLED")));
        Assert.True(WslClient.IsVirtualizationError(Result(1, stdout: "code 0x80370102")));
        Assert.False(WslClient.IsVirtualizationError(Result(1, stderr: "other")));
    }

    [Fact]
    public void IsNetworkError_detects_wininet_token()
    {
        Assert.True(WslClient.IsNetworkError(Result(1, stderr: "Error: 0x80072ee7 WININET_E_NAME_NOT_RESOLVED")));
        Assert.True(WslClient.IsNetworkError(Result(1, stdout: "WININET_E_TIMEOUT")));
        Assert.False(WslClient.IsNetworkError(Result(1, stderr: "other")));
    }

    [Fact]
    public async Task IsAvailable_false_when_wsl_missing()
    {
        var runner = new FakeProcessRunner();
        runner.ThrowNotFound("wsl.exe");

        Assert.False(await new WslClient(runner).IsAvailableAsync(Ct));
    }

    [Fact]
    public async Task IsAvailable_true_on_exit_zero()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("wsl.exe", Result());

        Assert.True(await new WslClient(runner).IsAvailableAsync(Ct));
        Assert.Equal(["--status"], runner.Calls[0].Arguments);
    }
}
