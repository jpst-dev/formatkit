namespace FormatKit.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void Cleanup_journal_lives_under_root()
    {
        var paths = new AppPaths(Path.Combine("root", "FormatKit"));

        Assert.Equal(Path.Combine("root", "FormatKit", "cleanup-journal.json"), paths.CleanupJournalFile);
    }
}
