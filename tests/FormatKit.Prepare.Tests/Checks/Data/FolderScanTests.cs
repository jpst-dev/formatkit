using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Checks.Data;

namespace FormatKit.Prepare.Tests.Checks.Data;

public class FolderScanTests
{
    [Fact]
    public void A_cancelled_scan_stops()
    {
        var files = new InMemoryFileSystem();
        files.SeedEntry(@"C:\Users\joao\Downloads\a.bin", 10);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => FolderScan.Run(files, @"C:\Users\joao\Downloads", cts.Token));
    }

    [Fact]
    public void Counts_the_local_files()
    {
        var files = new InMemoryFileSystem();
        files.SeedEntry(@"C:\Users\joao\Downloads\a.bin", 10);
        files.SeedEntry(@"C:\Users\joao\Downloads\sub\b.bin", 5);

        var scan = FolderScan.Run(files, @"C:\Users\joao\Downloads", CancellationToken.None);

        Assert.Equal(new FolderScan(15, 2, 0), scan);
    }
}
