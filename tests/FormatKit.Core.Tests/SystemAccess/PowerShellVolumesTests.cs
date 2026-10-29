using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class PowerShellVolumesTests
{
    private static ProcessResult Result(int exit, string stdout = "", string stderr = "") =>
        new(exit, stdout, stderr, TimeSpan.Zero, false);

    private static PowerShellVolumes Create(ProcessResult result)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("powershell.exe", result);
        return new PowerShellVolumes(runner);
    }

    [Fact]
    public async Task ParsesVolumes()
    {
        var json = """[{"Letter":"C","Disk":0,"BitLocker":1,"Vhdx":null,"Recovery":null},{"Letter":"D","Disk":null,"BitLocker":6,"Vhdx":"C:\\DevDrive\\dev.vhdx","Recovery":true}]""";

        var volumes = await Create(Result(0, json)).ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, volumes.Count);
        Assert.Equal(new VolumeInfo('C', 0, BitLockerState.On, null, null), volumes[0]);
        Assert.Equal(new VolumeInfo('D', null, BitLockerState.Locked, @"C:\DevDrive\dev.vhdx", true), volumes[1]);
    }

    [Fact]
    public async Task SingleObject_IsOneVolume()
    {
        var json = """{"Letter":"C","Disk":0,"BitLocker":2,"Vhdx":null,"Recovery":false}""";

        var volumes = await Create(Result(0, json)).ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new VolumeInfo('C', 0, BitLockerState.Off, null, false), Assert.Single(volumes));
    }

    [Theory]
    [InlineData("1", BitLockerState.On)]
    [InlineData("2", BitLockerState.Off)]
    [InlineData("3", BitLockerState.Encrypting)]
    [InlineData("4", BitLockerState.Decrypting)]
    [InlineData("5", BitLockerState.Suspended)]
    [InlineData("6", BitLockerState.Locked)]
    [InlineData("0", BitLockerState.Unknown)]
    [InlineData("null", BitLockerState.Unknown)]
    public async Task MapsBitLocker(string value, BitLockerState expected)
    {
        var json = "{\"Letter\":\"C\",\"Disk\":0,\"BitLocker\":" + value + ",\"Vhdx\":null,\"Recovery\":null}";

        var volumes = await Create(Result(0, json)).ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, Assert.Single(volumes).BitLocker);
    }

    [Fact]
    public async Task NonZeroExit_ThrowsWithExitCode()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Create(Result(7, "", "boom")).ListAsync(TestContext.Current.CancellationToken));

        Assert.Contains("7", ex.Message);
    }

    [Fact]
    public async Task InvalidJson_ThrowsWithExitCode()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Create(Result(0, "not json")).ListAsync(TestContext.Current.CancellationToken));

        Assert.Contains("exit 0", ex.Message);
    }

    [Fact]
    public void Script_FallsBackToWin32ForDiskNumber_WithoutLocalizedNames()
    {
        var script = PowerShellVolumes.Script;

        Assert.Contains("Win32_LogicalDiskToPartition", script);
        Assert.Contains("Disk #", script);
        Assert.DoesNotContain("Antecedent.Name", script);
        Assert.Contains("[int]$d.BusType -eq 15", script);
    }

    [Fact]
    public async Task EmptyOutput_IsNoVolumes()
    {
        var volumes = await Create(Result(0, "")).ListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(volumes);
    }
}
