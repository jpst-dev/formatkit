using System.Globalization;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Checks.Data;

namespace FormatKit.Prepare.Tests.Checks.Data;

public class VolumesCheckTests : IDisposable
{
    private readonly CultureInfo _saved = CultureInfo.CurrentUICulture;

    public VolumesCheckTests() => CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _saved;

    private static VolumeInfo Vol(char letter, int? disk, string? vhdx = null) =>
        new(letter, disk, BitLockerState.Off, vhdx, null);

    private static async Task<IReadOnlyList<PrepareItem>> RunAsync(params VolumeInfo[] volumes)
    {
        var context = PrepareTestContext.Create(TestServices.Create(volumes: new FakeVolumes(volumes)), volumes);
        return await new VolumesCheck().RunAsync(context, CancellationToken.None);
    }

    [Fact]
    public void Identity()
    {
        var check = new VolumesCheck();
        Assert.Equal("data.volumes", check.Id);
        Assert.Equal(PrepareGroup.Data, check.Group);
    }

    [Fact]
    public async Task Volume_on_the_windows_disk_is_attention_and_other_disk_is_ok()
    {
        var items = await RunAsync(Vol('C', 0), Vol('D', 0), Vol('E', 1));

        var d = Assert.Single(items, i => i.Title.Contains("D:"));
        Assert.Equal(PrepareStatus.Attention, d.Status);
        Assert.Contains("delete only the Windows partitions", d.Action);
        Assert.Contains("D:", d.Action);
        var e = Assert.Single(items, i => i.Title.Contains("E:"));
        Assert.Equal(PrepareStatus.Ok, e.Status);
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal("data.volumes", i.CheckId));
    }

    [Fact]
    public async Task Unknown_disk_number_gives_one_not_checked()
    {
        var items = await RunAsync(Vol('C', 0), Vol('D', null), Vol('E', null));

        var item = Assert.Single(items);
        Assert.Equal(PrepareStatus.NotChecked, item.Status);
    }

    [Fact]
    public async Task Volume_backed_by_a_lost_vhdx_gives_no_item()
    {
        var items = await RunAsync(Vol('C', 0), Vol('D', null, @"C:\dev.vhdx"), Vol('E', 1));

        var item = Assert.Single(items);
        Assert.Contains("E:", item.Title);
    }

    [Fact]
    public async Task Only_the_windows_volume_gives_no_item()
    {
        Assert.Empty(await RunAsync(Vol('C', 0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unknown_volume_list_is_one_not_checked_item(bool listWithoutSystemDrive)
    {
        var windows = new WindowsVolume('C', listWithoutSystemDrive ? [Vol('D', 0)] : null);
        var context = PrepareTestContext.Create(windows: windows);

        var item = Assert.Single(await new VolumesCheck().RunAsync(context, CancellationToken.None));

        Assert.Equal(PrepareStatus.NotChecked, item.Status);
        Assert.Equal("Disk layout not checked", item.Title);
        Assert.Contains("could not be read", item.Detail);
    }

    [Fact]
    public async Task Reads_the_volume_list_shared_in_the_context()
    {
        var services = TestServices.Create(volumes: new FakeVolumes { Throw = new InvalidOperationException("second read") });
        var context = PrepareTestContext.Create(services, [Vol('C', 0), Vol('D', 1)]);

        var item = Assert.Single(await new VolumesCheck().RunAsync(context, CancellationToken.None));

        Assert.Equal(PrepareStatus.Ok, item.Status);
    }
}
