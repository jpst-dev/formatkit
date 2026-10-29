using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Media.Usb;

namespace FormatKit.Media.Tests.Usb;

public class UsbTargetTests
{
    private const long Gb = 1_000_000_000;
    private static readonly string[] NoSources = [];

    private static DiskInfo Stick(long size = 16 * Gb, string id = "id3", string bus = "USB", bool removable = true,
        bool boot = false, char? letter = 'E') =>
        new(3, id, "SanDisk", size, bus, removable, boot, false, [new DiskVolume(letter, "STICK")]);

    private static UsbRefusal Refusal(DiskInfo d, bool allow = false, string[]? sources = null, long required = 9 * Gb) =>
        UsbEligibility.Check(d, allow, sources ?? NoSources, required).Refusal;

    [Fact]
    public void Eligible_stick_has_no_refusal() => Assert.Equal(UsbRefusal.None, Refusal(Stick()));

    [Fact]
    public void Nvme_is_not_usb() => Assert.Equal(UsbRefusal.NotUsb, Refusal(Stick(bus: "NVMe")));

    [Fact]
    public void Usb_boot_disk_is_refused() => Assert.Equal(UsbRefusal.BootOrSystem, Refusal(Stick(boot: true)));

    [Fact]
    public void Non_removable_usb_is_a_hard_disk_unless_allowed()
    {
        Assert.Equal(UsbRefusal.HardDisk, Refusal(Stick(removable: false)));
        Assert.Equal(UsbRefusal.None, Refusal(Stick(removable: false), allow: true));
    }

    [Theory]
    [InlineData(@"E:\profile.json")]
    [InlineData(@"e:\iso\win.iso")]
    [InlineData(@"E:\inputs\ssh")]
    public void Source_on_the_stick_is_refused_and_listed(string path)
    {
        var c = UsbEligibility.Check(Stick(), false, [@"C:\other\a.json", path], 9 * Gb);
        Assert.Equal(UsbRefusal.HoldsSourceFiles, c.Refusal);
        Assert.Equal([path], c.SourcesOnDisk);
    }

    [Theory]
    [InlineData(@"\\?\E:\win.iso")]
    [InlineData(@"\\.\E:\profile.json")]
    [InlineData(@"E:profile.json")]
    public void Extended_and_drive_relative_paths_on_the_stick_are_refused(string path)
    {
        var c = UsbEligibility.Check(Stick(), false, [path], 9 * Gb);
        Assert.Equal(UsbRefusal.HoldsSourceFiles, c.Refusal);
        Assert.Equal([path], c.SourcesOnDisk);
    }

    [Theory]
    [InlineData(@"profile.json")]
    [InlineData(@"..\iso\win.iso")]
    public void Relative_path_resolves_against_the_current_directory(string path)
    {
        var cwdLetter = Directory.GetCurrentDirectory()[0];
        var c = UsbEligibility.Check(Stick(letter: cwdLetter), false, [path], 9 * Gb);
        Assert.Equal(UsbRefusal.HoldsSourceFiles, c.Refusal);
        Assert.Equal([path], c.SourcesOnDisk);
    }

    [Fact]
    public void Extended_unc_never_matches() =>
        Assert.Equal(UsbRefusal.None, Refusal(Stick(), sources: [@"\\?\UNC\nas\iso\win.iso"]));

    [Fact]
    public void Unc_source_never_matches() =>
        Assert.Equal(UsbRefusal.None, Refusal(Stick(), sources: [@"\\nas\iso\win.iso"]));

    [Fact]
    public void Too_small_stick_is_refused() => Assert.Equal(UsbRefusal.TooSmall, Refusal(Stick(size: 8 * Gb)));

    [Fact]
    public void Large_stick_is_judged_by_the_capped_size()
    {
        Assert.Equal(UsbRefusal.None, Refusal(Stick(size: 64 * Gb)));
        Assert.Equal(UsbRefusal.TooSmall, Refusal(Stick(size: 64 * Gb), required: 40 * Gb));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Disk_without_a_unique_id_is_refused(string id) =>
        Assert.Equal(UsbRefusal.NoUniqueId, Refusal(Stick(id: id)));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Confirmed_disk_without_a_unique_id_is_never_prepared(string id)
    {
        // Without an id the swap check cannot tell two sticks apart: refuse even a candidate that claims no refusal.
        var fake = new FakeDisks(Stick(id: id));
        var target = new UsbTarget(fake);
        var confirmed = new UsbCandidate(Stick(id: id), UsbRefusal.None, []);

        await Assert.ThrowsAsync<DiskChangedException>(() => target.PrepareAsync(confirmed, false, NoSources, 9 * Gb, TestContext.Current.CancellationToken));
        Assert.Empty(fake.Prepared);
    }

    [Fact]
    public async Task Unchanged_disk_is_prepared()
    {
        var fake = new FakeDisks(Stick());
        var (target, c) = await Listed(fake, NoSources);
        var root = await target.PrepareAsync(c, false, NoSources, 9 * Gb, TestContext.Current.CancellationToken);
        Assert.Equal(@"F:\", root);
        Assert.Equal([(3, "id3")], fake.Prepared);
    }

    [Fact]
    public async Task Swapped_stick_is_not_erased()
    {
        var fake = new FakeDisks(Stick());
        var (target, c) = await Listed(fake, NoSources);
        fake.BeforeSecondList = () => fake.Disks[0] = Stick(id: "other");
        await Assert.ThrowsAsync<DiskChangedException>(() => target.PrepareAsync(c, false, NoSources, 9 * Gb, TestContext.Current.CancellationToken));
        Assert.Empty(fake.Prepared);
    }

    [Fact]
    public async Task Removed_stick_is_not_erased()
    {
        var fake = new FakeDisks(Stick());
        var (target, c) = await Listed(fake, NoSources);
        fake.BeforeSecondList = () => fake.Disks.Clear();
        await Assert.ThrowsAsync<DiskChangedException>(() => target.PrepareAsync(c, false, NoSources, 9 * Gb, TestContext.Current.CancellationToken));
        Assert.Empty(fake.Prepared);
    }

    [Fact]
    public async Task Profile_copied_onto_the_stick_in_between_is_not_erased()
    {
        var fake = new FakeDisks(Stick(letter: 'G'));
        string[] sources = [@"E:\profile.json"];
        var (target, c) = await Listed(fake, sources);
        Assert.Equal(UsbRefusal.None, c.Refusal);
        fake.BeforeSecondList = () => fake.Disks[0] = Stick(letter: 'E');
        await Assert.ThrowsAsync<DiskChangedException>(() => target.PrepareAsync(c, false, sources, 9 * Gb, TestContext.Current.CancellationToken));
        Assert.Empty(fake.Prepared);
    }

    private static async Task<(UsbTarget, UsbCandidate)> Listed(FakeDisks fake, string[] sources)
    {
        var target = new UsbTarget(fake);
        return (target, Assert.Single(await target.ListAsync(false, sources, 9 * Gb, TestContext.Current.CancellationToken)));
    }
}
