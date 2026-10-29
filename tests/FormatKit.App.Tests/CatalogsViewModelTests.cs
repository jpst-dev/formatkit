using FormatKit.App.ViewModels;
using FormatKit.Cleanup.Catalog;
using FormatKit.Programs.Catalog;

namespace FormatKit.App.Tests;

public class CatalogsViewModelTests
{
    private static CleanupItem Cleanup(string id, CleanupLevel level, string title, bool contested = false)
    {
        var category = Enum.Parse<CleanupCategory>(id[..id.IndexOf('.')], ignoreCase: true);
        var text = new ItemText(title, "what " + id, "breaks " + id, "gain " + id);
        return new CleanupItem(id, category, level, contested, false, ItemScope.Machine, false,
            new Dictionary<string, ItemText> { ["en"] = text, ["pt-BR"] = text }, []);
    }

    private static CatalogProgram Program(string id, ProgramCategory category, string name)
    {
        var text = new ProgramText(name, "about " + name);
        return new CatalogProgram(id, category, "Vendor." + id, "winget",
            new Dictionary<string, ProgramText> { ["en"] = text, ["pt-BR"] = text });
    }

    private static CatalogsViewModel Create() => new(
        new CleanupCatalog(1,
        [
            Cleanup("privacy.telemetry", CleanupLevel.Safe, "Reduce Telemetry"),
            Cleanup("apps.bloat", CleanupLevel.Balanced, "Remove Bloat", contested: true),
            Cleanup("developer.mode", CleanupLevel.Safe, "Turn on Developer Mode"),
            Cleanup("performance.visual", CleanupLevel.Aggressive, "Visual Effects"),
        ]),
        new ProgramCatalog(1,
        [
            Program("firefox", ProgramCategory.Browsers, "Firefox"),
            Program("vlc", ProgramCategory.Media, "VLC Player"),
            Program("git", ProgramCategory.Development, "Git"),
        ]));

    private static IEnumerable<string> Ids(CatalogsViewModel vm) => vm.CleanupItems.Select(i => i.Id);

    [Fact]
    public void NoFilter_ShowsEverything()
    {
        var vm = Create();

        Assert.Equal(4, vm.CleanupItems.Count);
        Assert.Equal(3, vm.ProgramItems.Count);
    }

    [Fact]
    public void LevelFilter_IsCumulative()
    {
        var vm = Create();

        vm.LevelFilter = CleanupLevel.Balanced;

        Assert.Equal(["privacy.telemetry", "apps.bloat", "developer.mode"], Ids(vm));
    }

    [Fact]
    public void Search_MatchesIdOrTitle_CaseInsensitive()
    {
        var vm = Create();

        vm.SearchText = "TELEMETRY";
        Assert.Equal(["privacy.telemetry"], Ids(vm));

        vm.SearchText = "remove bloat";
        Assert.Equal(["apps.bloat"], Ids(vm));

        vm.SearchText = "vlc";
        Assert.Equal(["vlc"], vm.ProgramItems.Select(p => p.Id));
        Assert.Empty(vm.CleanupItems);

        vm.SearchText = "";
        Assert.Equal(4, vm.CleanupItems.Count);
    }

    [Fact]
    public void CategoryFilter_Programs()
    {
        var vm = Create();

        vm.ProgramCategoryFilter = ProgramCategory.Browsers;

        Assert.Equal(["firefox"], vm.ProgramItems.Select(p => p.Id));
        Assert.Equal(4, vm.CleanupItems.Count);
    }

    [Fact]
    public void DeveloperCategory_FollowsCleanupListLevelSemantics()
    {
        // `cleanup list --level safe` shows developer items of level Safe; --category developer narrows to them.
        var vm = Create();

        vm.LevelFilter = CleanupLevel.Safe;
        Assert.Contains("developer.mode", Ids(vm));

        vm.CleanupCategoryFilter = CleanupCategory.Developer;
        Assert.Equal(["developer.mode"], Ids(vm));

        vm.LevelFilter = null;
        vm.CleanupCategoryFilter = CleanupCategory.Privacy;
        Assert.Equal(["privacy.telemetry"], Ids(vm));
    }

    [Fact]
    public void Selected_ShowsDetailsOfTheActiveTab()
    {
        var vm = Create();
        Assert.Null(vm.Details);

        vm.SelectedCleanup = vm.CleanupItems.Single(i => i.Id == "apps.bloat");

        Assert.NotNull(vm.Details);
        Assert.Equal("Remove Bloat", vm.Details.Title);
        Assert.Contains(vm.Details.Lines, l => l.Contains("what apps.bloat"));
        Assert.Contains(vm.Details.Lines, l => l.Contains("gain apps.bloat"));

        vm.SelectedTab = 1;
        Assert.Null(vm.Details);
        Assert.Null(vm.Selected);

        vm.SelectedProgram = vm.ProgramItems.Single(p => p.Id == "git");
        Assert.Equal("Git", vm.Details!.Title);
        Assert.Contains(vm.Details.Lines, l => l.Contains("Vendor.git"));

        vm.SelectedTab = 0;
        Assert.Equal("Remove Bloat", vm.Details!.Title);
    }

    [Fact]
    public void OtherTabListPushingNull_DoesNotLoseTheSelection()
    {
        var vm = Create();
        vm.SelectedCleanup = vm.CleanupItems.Single(i => i.Id == "apps.bloat");
        vm.SelectedTab = 1;

        vm.SelectedProgram = null; // the Programs list reports "nothing selected" when it appears

        vm.SelectedTab = 0;
        Assert.Equal("Remove Bloat", vm.Details!.Title);
    }

    [Fact]
    public void Filtering_ClearsSelectionsThatDisappear()
    {
        var vm = Create();
        vm.SelectedTab = 1;
        vm.SelectedProgram = vm.ProgramItems.Single(p => p.Id == "git");

        vm.SearchText = "firefox";

        Assert.Null(vm.SelectedProgram);
        Assert.Null(vm.Details);
    }

    [Fact]
    public void EveryFilterValue_HasAResourceInBothLanguages()
    {
        var keys = Enum.GetValues<CleanupLevel>().Select(v => "Level_" + v)
            .Concat(Enum.GetValues<CleanupCategory>().Select(v => "CleanupCategory_" + v))
            .Concat(Enum.GetValues<ProgramCategory>().Select(v => "ProgramCategory_" + v))
            .Append("Cat_Context_Machine").Append("Cat_Context_User");
        var manager = new System.Resources.ResourceManager("FormatKit.App.Resources.Strings", typeof(FormatKit.App.Localization.LocExtension).Assembly);
        foreach (var culture in new[] { "en", "pt-BR" })
            foreach (var key in keys)
                Assert.False(string.IsNullOrEmpty(manager.GetString(key, System.Globalization.CultureInfo.GetCultureInfo(culture))), $"{key} ({culture})");
    }
}
