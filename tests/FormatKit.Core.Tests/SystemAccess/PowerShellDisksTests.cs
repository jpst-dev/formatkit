using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class PowerShellDisksTests
{
    private static ProcessResult Result(int exit, string stdout = "", string stderr = "") =>
        new(exit, stdout, stderr, TimeSpan.Zero, false);

    private static (PowerShellDisks Disks, FakeProcessRunner Runner) Create(ProcessResult result)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("powershell.exe", result);
        return (new PowerShellDisks(runner), runner);
    }

    private static string Script(FakeProcessRunner runner) => runner.Calls[0].Arguments[^1];

    private const string TwoDisks = """
        [
          {"Number":1,"UniqueId":"USB-1","FriendlyName":"SanDisk Cruzer","Size":16013942784,"BusType":"USB","BusTypeValue":7,
           "IsBoot":false,"IsSystem":false,"MediaType":"Removable Media","Capabilities":[3,4,7],
           "Volumes":[{"Letter":"e","Label":"STICK"},{"Letter":null,"Label":""}]},
          {"Number":0,"UniqueId":"NVME-0","FriendlyName":"Samsung SSD","Size":512110190592,"BusType":"NVMe","BusTypeValue":17,
           "IsBoot":true,"IsSystem":true,"MediaType":"Fixed hard disk media","Capabilities":[3,4],
           "Volumes":{"Letter":"C","Label":"Windows"}}
        ]
        """;

    [Fact]
    public async Task List_ParsesUsbStickAndSystemDisk()
    {
        var (disks, runner) = Create(Result(0, TwoDisks));

        var list = await disks.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, list.Count);
        var usb = list[0];
        Assert.Equal(1, usb.Number);
        Assert.Equal("USB-1", usb.UniqueId);
        Assert.Equal("SanDisk Cruzer", usb.FriendlyName);
        Assert.Equal(16013942784L, usb.SizeBytes);
        Assert.Equal("USB", usb.BusType);
        Assert.True(usb.IsRemovable);
        Assert.False(usb.IsBoot);
        Assert.Equal(new DiskVolume('E', "STICK"), usb.Volumes[0]);
        Assert.Equal(new DiskVolume(null, null), usb.Volumes[1]);
        var nvme = list[1];
        Assert.Equal("NVMe", nvme.BusType);
        Assert.False(nvme.IsRemovable);
        Assert.True(nvme.IsBoot);
        Assert.True(nvme.IsSystem);
        Assert.Equal(new DiskVolume('C', "Windows"), Assert.Single(nvme.Volumes));
        Assert.Equal(Timeouts.Query, runner.Calls[0].Timeout);
        Assert.Contains("$ProgressPreference = 'SilentlyContinue'", Script(runner));
    }

    [Fact]
    public async Task List_NumericBusTypeAndNoPartitions()
    {
        var (disks, _) = Create(Result(0, """[{"Number":2,"UniqueId":"u","FriendlyName":"X","Size":1000,"BusType":7,"MediaType":"Removable Media","Capabilities":[],"Volumes":[]}]"""));

        var disk = Assert.Single(await disks.ListAsync(TestContext.Current.CancellationToken));

        Assert.Equal("USB", disk.BusType);
        Assert.True(disk.IsRemovable);
        Assert.Empty(disk.Volumes);
    }

    [Theory]
    [InlineData("USB", "Removable Media", "[]", true)]
    [InlineData("USB", "External hard disk media", "[3,4,7]", false)]
    [InlineData("USB", "Unknown", "[3,4,7]", true)]
    [InlineData("USB", null, "[3,4]", false)]
    [InlineData("SATA", "Removable Media", "[7]", false)]
    [InlineData("NVMe", "Fixed hard disk media", "[]", false)]
    public void IsRemovable_FollowsRuleR7(string bus, string? media, string capabilities, bool expected)
    {
        var caps = System.Text.Json.JsonSerializer.Deserialize<int[]>(capabilities)!;

        Assert.Equal(expected, PowerShellDisks.IsRemovable(bus, media, caps));
    }

    [Fact]
    public async Task List_NonZeroExit_Throws()
    {
        var (disks, _) = Create(Result(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => disks.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task List_EmptyOutput_Throws()
    {
        var (disks, _) = Create(Result(0, "  "));

        await Assert.ThrowsAsync<InvalidOperationException>(() => disks.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task List_MalformedJson_Throws()
    {
        var (disks, _) = Create(Result(0, "{not json"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => disks.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task List_ScriptDoesNotDependOnGetDisk()
    {
        // Found in scenario 27: on the author's PC Get-Disk and Get-Partition return nothing, even elevated.
        var (disks, runner) = Create(Result(0, "[]"));

        await disks.ListAsync(TestContext.Current.CancellationToken);

        var script = Script(runner);
        Assert.DoesNotContain("Get-Disk", script);
        Assert.DoesNotContain("Get-Partition", script);
        Assert.Contains("Get-PhysicalDisk", script);
        Assert.Contains("Win32_DiskDrive", script);
    }

    [Fact]
    public async Task List_ScriptTreatsADiskWithoutWin32DataAsSystem()
    {
        var (disks, runner) = Create(Result(0, "[]"));

        await disks.ListAsync(TestContext.Current.CancellationToken);

        var script = Script(runner);
        var assumed = script.IndexOf("$isSystem = $true", StringComparison.Ordinal);
        Assert.True(assumed >= 0, script);
        Assert.True(assumed < script.IndexOf("'GPT: System'", StringComparison.Ordinal), script);
        Assert.Contains("$windows", script);
    }

    [Fact]
    public async Task List_And_prepare_compute_the_same_identity_from_the_device_and_its_serial()
    {
        // Found in scenario 27: Get-PhysicalDisk gives a SanDisk stick the UniqueId " USB", which tells no stick from another.
        var (listed, listRunner) = Create(Result(0, "[]"));
        await listed.ListAsync(TestContext.Current.CancellationToken);
        var (prepared, prepareRunner) = Create(Result(0, "F\r\n"));
        await prepared.PrepareUsbAsync(3, "x", TestContext.Current.CancellationToken);

        Assert.Contains(PowerShellDisks.IdentityScript, Script(listRunner));
        Assert.Contains(PowerShellDisks.IdentityScript, Script(prepareRunner));
        Assert.Contains("PNPDeviceID", PowerShellDisks.IdentityScript);
        Assert.Contains("SerialNumber", PowerShellDisks.IdentityScript);
        Assert.DoesNotContain("$d.UniqueId", Script(listRunner));
        Assert.DoesNotContain("$d.UniqueId", Script(prepareRunner));
    }

    [Fact]
    public async Task Prepare_ScriptChecksTheIdentityBeforeDiskpart()
    {
        var (disks, runner) = Create(Result(0, "F\r\n"));

        var root = await disks.PrepareUsbAsync(3, "ab'cd", TestContext.Current.CancellationToken);

        Assert.Equal(@"F:\", root);
        var script = Script(runner);
        var steps = new[]
        {
            "Get-PhysicalDisk",
            "Win32_DiskDrive -Filter 'Index=3'",
            "[string]::IsNullOrWhiteSpace($id)",
            "$id -cne 'ab''cd') { exit 4 }",
            "'select disk 3'",
            "'clean'",
            "'convert mbr'",
            "('create partition primary size=' + $mb)",
            "'active'",
            "'format fs=fat32 label=FORMATKIT quick'",
            "'assign'",
            "diskpart /s $f",
            "Win32_LogicalDisk",
        };
        var last = -1;
        foreach (var step in steps)
        {
            var at = script.IndexOf(step, StringComparison.Ordinal);
            Assert.True(at > last, $"'{step}' missing or out of order in: {script}");
            last = at;
        }

        Assert.Contains("34359738368", script);
        Assert.Contains("$ProgressPreference = 'SilentlyContinue'", script);
        Assert.Equal(Timeouts.Media, runner.Calls[0].Timeout);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Prepare_ScriptDoesNotDependOnGetDisk()
    {
        var (disks, runner) = Create(Result(0, "F\r\n"));

        await disks.PrepareUsbAsync(3, "x", TestContext.Current.CancellationToken);

        var script = Script(runner);
        foreach (var cmdlet in new[] { "Get-Disk", "Clear-Disk", "Initialize-Disk", "New-Partition", "Format-Volume", "Get-Partition" })
            Assert.DoesNotContain(cmdlet, script);
    }

    [Fact]
    public async Task Prepare_ScriptExits4WhenTheDiskHasNoIdentity()
    {
        // An empty id on both sides would match: the script must refuse it before diskpart.
        var (disks, runner) = Create(Result(0, "F\r\n"));

        await disks.PrepareUsbAsync(3, "", TestContext.Current.CancellationToken);

        var script = Script(runner);
        var guard = script.IndexOf("if ([string]::IsNullOrWhiteSpace($id) -or", StringComparison.Ordinal);
        Assert.True(guard >= 0, script);
        Assert.True(guard < script.IndexOf("'clean'", StringComparison.Ordinal), script);
        Assert.True(guard < script.IndexOf("diskpart /s", StringComparison.Ordinal), script);
    }

    [Fact]
    public async Task Prepare_Exit4_ThrowsDiskChanged()
    {
        var (disks, _) = Create(Result(4));

        await Assert.ThrowsAsync<DiskChangedException>(() => disks.PrepareUsbAsync(1, "x", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Prepare_OtherFailure_Throws()
    {
        var (disks, _) = Create(Result(1, "", "boom"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => disks.PrepareUsbAsync(1, "x", TestContext.Current.CancellationToken));
        Assert.IsNotType<DiskChangedException>(ex);
        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public async Task Prepare_NoLetter_Throws()
    {
        var (disks, _) = Create(Result(0, "  "));

        await Assert.ThrowsAsync<InvalidOperationException>(() => disks.PrepareUsbAsync(1, "x", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void MaxPartitionBytes_Is32Gb() => Assert.Equal(34359738368L, PowerShellDisks.MaxPartitionBytes);
}
