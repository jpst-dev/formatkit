using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class PowerShellWindowsReadinessTests
{
    private static ProcessResult Result(int exit, string stdout = "") => new(exit, stdout, "", TimeSpan.Zero, false);

    private static (PowerShellWindowsReadiness Readiness, FakeProcessRunner Runner) Create(ProcessResult result)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("powershell.exe", result);
        return (new PowerShellWindowsReadiness(runner), runner);
    }

    [Fact]
    public async Task Read_ParsesEveryField()
    {
        var (readiness, _) = Create(Result(0, """{"Build":19045,"Ram":4294967296,"Firmware":"UEFI","SecureBoot":1,"Tpm":"2.0, 0, 1.38"}"""));

        var info = await readiness.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new ReadinessInfo(19045, 4L << 30, true, true, 2), info);
    }

    [Fact]
    public async Task Read_LegacyFirmwareIsNotUefi()
    {
        var (readiness, _) = Create(Result(0, """{"Build":19045,"Ram":4294967296,"Firmware":"Legacy","SecureBoot":0,"Tpm":"1.2, 0, 1.16"}"""));

        var info = await readiness.ReadAsync(TestContext.Current.CancellationToken);

        Assert.False(info.Uefi);
        Assert.False(info.SecureBoot);
        Assert.Equal(1, info.TpmMajorVersion);
    }

    [Fact]
    public async Task Read_NullsStayNull()
    {
        var (readiness, _) = Create(Result(0, """{"Build":22631,"Ram":null,"Firmware":null,"SecureBoot":null,"Tpm":null}"""));

        var info = await readiness.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new ReadinessInfo(22631, null, null, null, null), info);
    }

    [Fact]
    public async Task Read_ReadsTheTpmOnlyInsideAnElevationCheck()
    {
        var (readiness, runner) = Create(Result(0, """{"Build":1,"Ram":1,"Firmware":"UEFI","SecureBoot":1,"Tpm":null}"""));

        await readiness.ReadAsync(TestContext.Current.CancellationToken);

        var script = runner.Calls[0].Arguments[^1];
        var tpm = script.IndexOf("Win32_Tpm", StringComparison.Ordinal);
        var elevated = script.IndexOf("IsInRole", StringComparison.Ordinal);
        Assert.True(elevated >= 0 && tpm > elevated);
        Assert.Contains("root/cimv2/Security/MicrosoftTpm", script);
        Assert.Contains("ConvertTo-Json", script);
    }

    [Theory]
    [InlineData(1, "")]
    [InlineData(0, "")]
    [InlineData(0, "not json")]
    public async Task Read_FailureThrows(int exit, string stdout)
    {
        var (readiness, _) = Create(Result(exit == 0 ? 0 : 1, stdout));

        await Assert.ThrowsAsync<InvalidOperationException>(() => readiness.ReadAsync(TestContext.Current.CancellationToken));
    }
}
