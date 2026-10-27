using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class PowerShellAppxPackagesTests
{
    private static ProcessResult Result(int exit, string stdout = "", string stderr = "") =>
        new(exit, stdout, stderr, TimeSpan.Zero, false);

    private static (PowerShellAppxPackages Packages, FakeProcessRunner Runner) Create(ProcessResult result)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("powershell.exe", result);
        return (new PowerShellAppxPackages(runner), runner);
    }

    [Theory]
    [InlineData("True;False", true, false)]
    [InlineData("False;True\r\n", false, true)]
    [InlineData("True;True", true, true)]
    [InlineData("False;False", false, false)]
    public async Task GetState_parses_true_false(string output, bool installed, bool provisioned)
    {
        var (packages, _) = Create(Result(0, output));

        var state = await packages.GetStateAsync("Clipchamp.Clipchamp", TestContext.Current.CancellationToken);

        Assert.Equal(new AppxState(installed, provisioned), state);
    }

    [Fact]
    public async Task GetState_runs_powershell_with_exact_name()
    {
        var (packages, runner) = Create(Result(0, "False;False"));

        await packages.GetStateAsync("Clipchamp.Clipchamp", TestContext.Current.CancellationToken);

        var call = Assert.Single(runner.Calls);
        Assert.Equal(["-NoProfile", "-NonInteractive", "-Command"], call.Arguments.Take(3));
        Assert.Contains("-Name 'Clipchamp.Clipchamp'", call.Arguments[3]);
        Assert.Contains("DisplayName -eq 'Clipchamp.Clipchamp'", call.Arguments[3]);
    }

    [Fact]
    public async Task Invalid_package_name_throws()
    {
        var (packages, runner) = Create(Result(0, "False;False"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => packages.GetStateAsync("x'; Remove-Item C:\\", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => packages.RemoveAsync("x'; Remove-Item C:\\", TestContext.Current.CancellationToken));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Unparseable_output_throws()
    {
        var (packages, _) = Create(Result(0, "something else"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => packages.GetStateAsync("Clipchamp.Clipchamp", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetState_nonzero_exit_throws()
    {
        var (packages, _) = Create(Result(1, stderr: "erro"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => packages.GetStateAsync("Clipchamp.Clipchamp", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Remove_runs_both_removals()
    {
        var (packages, runner) = Create(Result(0));

        await packages.RemoveAsync("Clipchamp.Clipchamp", TestContext.Current.CancellationToken);

        var script = Assert.Single(runner.Calls).Arguments[3];
        Assert.Contains("Get-AppxPackage -Name 'Clipchamp.Clipchamp' | Remove-AppxPackage", script);
        Assert.Contains("Remove-AppxProvisionedPackage -Online", script);
    }

    [Fact]
    public async Task Remove_nonzero_exit_throws()
    {
        var (packages, _) = Create(Result(1, stderr: "erro"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => packages.RemoveAsync("Clipchamp.Clipchamp", TestContext.Current.CancellationToken));
    }
}
