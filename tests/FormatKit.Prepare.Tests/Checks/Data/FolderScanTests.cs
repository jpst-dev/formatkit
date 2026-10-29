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

    [Fact]
    public void Folder_settings_files_do_not_count()
    {
        // Windows puts a hidden system desktop.ini in every user folder; it is not the user's data.
        var files = new InMemoryFileSystem();
        files.SeedEntry(@"C:\Users\joao\Pictures\desktop.ini", 504, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive);
        files.SeedEntry(@"C:\Users\joao\Pictures\sub\Desktop.INI", 100, FileAttributes.Hidden | FileAttributes.System);

        var scan = FolderScan.Run(files, @"C:\Users\joao\Pictures", CancellationToken.None);

        Assert.Equal(new FolderScan(0, 0, 0), scan);
    }

    [Fact]
    public void A_visible_desktop_ini_counts()
    {
        var files = new InMemoryFileSystem();
        files.SeedEntry(@"C:\Users\joao\Documents\desktop.ini", 50);

        var scan = FolderScan.Run(files, @"C:\Users\joao\Documents", CancellationToken.None);

        Assert.Equal(new FolderScan(50, 1, 0), scan);
    }
}
