namespace FormatKit.Prepare.Tests;

public class PrepareChecksTests
{
    [Fact]
    public void All_lists_every_check_in_report_order_without_duplicates()
    {
        var ids = PrepareChecks.All().Select(c => c.Id).ToList();

        Assert.Equal(
            [
                "security.bitlocker", "security.activation", "security.windows11-requirements", "data.user-folders", "data.other-users", "data.volumes",
                "dev.git-repos", "dev.wsl", "dev.keys", "dev.devdrive",
                "apps.programs-export", "apps.network-drivers", "apps.profile-inputs", "apps.browsers",
            ],
            ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
