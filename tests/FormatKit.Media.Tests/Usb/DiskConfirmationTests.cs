using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Media.Usb;

namespace FormatKit.Media.Tests.Usb;

/// <summary>The one way to erase: the typed number first, then the re-check (spec 5.2, WPF UI spec 5.7).</summary>
public class DiskConfirmationTests
{
    private const long Gb = 1_000_000_000;
    private static readonly string[] NoSources = [];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DiskInfo Stick(string id = "id3", int number = 3) =>
        new(number, id, "SanDisk", 16 * Gb, "USB", true, false, false, [new DiskVolume('E', "STICK")]);

    private static UsbCandidate Candidate(DiskInfo disk) => UsbEligibility.Check(disk, false, NoSources, 9 * Gb);

    private sealed class Answer(string? typed) : IDiskConfirmation
    {
        public int Asked { get; private set; }

        public Task<string?> AskAsync(UsbCandidate disk, CancellationToken ct)
        {
            Asked++;
            return Task.FromResult(typed);
        }
    }

    private static Task<string?> Confirm(FakeDisks disks, UsbCandidate candidate, IDiskConfirmation answer) =>
        new UsbTarget(disks).ConfirmAndPrepareAsync(candidate, answer, false, NoSources, 9 * Gb, Ct);

    [Theory]
    [InlineData("3")]
    [InlineData(" 3 ")]
    public async Task Typed_number_erases_and_returns_the_root(string typed)
    {
        var disks = new FakeDisks(Stick());

        var root = await Confirm(disks, Candidate(Stick()), new Answer(typed));

        Assert.Equal(@"F:\", root);
        Assert.Equal([(3, "id3")], disks.Prepared);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("03")]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData(null)]
    public async Task Anything_else_cancels_without_erasing_or_listing(string? typed)
    {
        var disks = new FakeDisks(Stick());

        var root = await Confirm(disks, Candidate(Stick()), new Answer(typed));

        Assert.Null(root);
        Assert.Empty(disks.Prepared);
        Assert.Equal(0, disks.ListCalls);
    }

    [Fact]
    public async Task Refused_disk_is_never_asked_about_nor_erased()
    {
        var boot = Stick() with { IsBoot = true };
        var disks = new FakeDisks(boot);
        var answer = new Answer("3");

        var root = await Confirm(disks, Candidate(boot), answer);

        Assert.Null(root);
        Assert.Equal(0, answer.Asked);
        Assert.Empty(disks.Prepared);
    }

    [Fact]
    public async Task Disk_swapped_after_the_list_stops_without_erasing()
    {
        var disks = new FakeDisks(Stick(id: "other"));

        await Assert.ThrowsAsync<DiskChangedException>(() => Confirm(disks, Candidate(Stick()), new Answer("3")));

        Assert.Empty(disks.Prepared);
    }

    [Fact]
    public async Task Stop_during_the_recheck_cancels_without_erasing()
    {
        using var stop = new CancellationTokenSource();
        var disks = new FakeDisks(Stick()) { BeforeSecondList = stop.Cancel };
        await disks.ListAsync(Ct); // The next call is the "second list" that BeforeSecondList watches.

        var root = await new UsbTarget(disks).ConfirmAndPrepareAsync(
            Candidate(Stick()), new Answer("3"), false, NoSources, 9 * Gb, Ct, stop.Token);

        Assert.Null(root);
        Assert.Empty(disks.Prepared);
    }

    /// <summary>The stop lands after the query has already returned, the last moment before the erase.</summary>
    private sealed class StopAfterTheList(DiskInfo disk, CancellationTokenSource stop) : IDisks
    {
        public bool Erased { get; private set; }

        public Task<IReadOnlyList<DiskInfo>> ListAsync(CancellationToken ct)
        {
            stop.Cancel();
            return Task.FromResult<IReadOnlyList<DiskInfo>>([disk]);
        }

        public Task<string> PrepareUsbAsync(int number, string uniqueId, CancellationToken ct)
        {
            Erased = true;
            return Task.FromResult(@"F:\");
        }
    }

    [Fact]
    public async Task Stop_right_after_the_recheck_cancels_without_erasing()
    {
        using var stop = new CancellationTokenSource();
        var disks = new StopAfterTheList(Stick(), stop);

        var root = await new UsbTarget(disks).ConfirmAndPrepareAsync(
            Candidate(Stick()), new Answer("3"), false, NoSources, 9 * Gb, Ct, stop.Token);

        Assert.Null(root);
        Assert.False(disks.Erased);
    }

    [Fact]
    public void Listing_shows_usb_disks_and_counts_the_hidden_hard_disks()
    {
        var stick = Candidate(Stick());
        var hardDisk = Candidate(Stick(id: "hd", number: 4) with { IsRemovable = false });
        var nvme = Candidate(Stick(id: "nvme", number: 0) with { BusType = "NVMe" });

        var listing = UsbListing.Of([nvme, stick, hardDisk]);

        Assert.Equal([stick], listing.Shown);
        Assert.Equal(1, listing.HiddenHardDisks);
    }
}
