using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class PowerShellOptionalFeaturesTests
{
    private static ProcessResult Result(int exit, string stdout = "", string stderr = "") =>
        new(exit, stdout, stderr, TimeSpan.Zero, false);

    private static (PowerShellOptionalFeatures Features, FakeProcessRunner Runner) Create(ProcessResult result)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("powershell.exe", result);
        return (new PowerShellOptionalFeatures(runner), runner);
    }

    [Theory]
    [InlineData("Enabled", OptionalFeatureState.Enabled)]
    [InlineData("Disabled", OptionalFeatureState.Disabled)]
    [InlineData("DisabledWithPayloadRemoved", OptionalFeatureState.Disabled)]
    [InlineData("Absent", OptionalFeatureState.Absent)]
    public async Task GetState_maps_states(string output, OptionalFeatureState expected)
    {
        var (features, runner) = Create(Result(0, output + "\r\n"));

        var state = await features.GetStateAsync("Microsoft-Hyper-V", TestContext.Current.CancellationToken);

        Assert.Equal(expected, state);
        var call = Assert.Single(runner.Calls);
        Assert.Contains("-FeatureName 'Microsoft-Hyper-V'", call.Arguments[3]);
    }

    [Fact]
    public async Task GetState_unknown_output_throws()
    {
        var (features, _) = Create(Result(0, "Weird"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => features.GetStateAsync("Microsoft-Hyper-V", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Invalid_feature_name_throws()
    {
        var (features, runner) = Create(Result(0, "Enabled"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => features.GetStateAsync("x'; calc", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => features.SetStateAsync("x'; calc", true, TestContext.Current.CancellationToken));
        Assert.Empty(runner.Calls);
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("False\r\n", false)]
    public async Task SetState_returns_restart_needed(string output, bool expected)
    {
        var (features, _) = Create(Result(0, output));

        var restart = await features.SetStateAsync("Microsoft-Hyper-V", false, TestContext.Current.CancellationToken);

        Assert.Equal(expected, restart);
    }

    [Theory]
    [InlineData(true, "Enable-WindowsOptionalFeature")]
    [InlineData(false, "Disable-WindowsOptionalFeature")]
    public async Task SetState_uses_matching_cmdlet(bool enabled, string cmdlet)
    {
        var (features, runner) = Create(Result(0, "False"));

        await features.SetStateAsync("Microsoft-Hyper-V", enabled, TestContext.Current.CancellationToken);

        var script = Assert.Single(runner.Calls).Arguments[3];
        Assert.Contains($"{cmdlet} -Online -FeatureName 'Microsoft-Hyper-V' -NoRestart", script);
    }

    [Fact]
    public async Task SetState_nonzero_exit_throws()
    {
        var (features, _) = Create(Result(1, stderr: "erro"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => features.SetStateAsync("Microsoft-Hyper-V", true, TestContext.Current.CancellationToken));
    }
}
