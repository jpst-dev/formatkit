using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class CimLicensingTests
{
    private static ProcessResult Result(int exit, string stdout = "") =>
        new(exit, stdout, "", TimeSpan.Zero, false);

    private static (CimLicensing Licensing, FakeProcessRunner Runner) Create(ProcessResult result)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("powershell.exe", result);
        return (new CimLicensing(runner), runner);
    }

    [Fact]
    public async Task ParsesActivation()
    {
        var (licensing, _) = Create(Result(0, """{"LicenseStatus":1,"ProductKeyChannel":"Retail"}"""));

        var value = await licensing.GetWindowsActivationAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new WindowsActivation(true, "Retail"), value);
    }

    [Fact]
    public async Task OtherStatus_IsNotActivated()
    {
        var (licensing, _) = Create(Result(0, """{"LicenseStatus":5,"ProductKeyChannel":"OEM:DM"}"""));

        var value = await licensing.GetWindowsActivationAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new WindowsActivation(false, "OEM:DM"), value);
    }

    [Fact]
    public async Task EmptyOutput_IsNull()
    {
        var (licensing, _) = Create(Result(0, "  \r\n"));

        Assert.Null(await licensing.GetWindowsActivationAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NonZeroExit_ThrowsWithExitCode()
    {
        var (licensing, _) = Create(Result(3));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => licensing.GetWindowsActivationAsync(TestContext.Current.CancellationToken));
        Assert.Contains("3", ex.Message);
    }

    [Fact]
    public async Task NeverSelectsKeys()
    {
        var (licensing, runner) = Create(Result(0, """{"LicenseStatus":1,"ProductKeyChannel":"Retail"}"""));

        await licensing.GetWindowsActivationAsync(TestContext.Current.CancellationToken);

        var script = Assert.Single(runner.Calls).Arguments[3];
        Assert.DoesNotContain("OA3xOriginalProductKey", script);
        var select = script[script.IndexOf("Select-Object", StringComparison.Ordinal)..];
        Assert.DoesNotContain("PartialProductKey", select);
        Assert.DoesNotContain("ProductKey", select.Replace("ProductKeyChannel", ""));
    }
}
