using System.Xml.Linq;
using FormatKit.App.ViewModels;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.App.Tests;

public class HomeViewModelTests
{
    [Theory]
    [InlineData("Strings.resx", "media")]
    [InlineData("Strings.pt-BR.resx", "mídia")]
    public void BeforeText_DoesNotPromiseTheMediaScreen(string file, string word)
    {
        // The installation media screen comes in a later phase; its navigation entry is hidden until then.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "src", "FormatKit.App", "Resources", file)))
            root = root.Parent ?? throw new InvalidOperationException("repository root not found");
        var text = XDocument.Load(Path.Combine(root.FullName, "src", "FormatKit.App", "Resources", file)).Root!
            .Elements("data").Single(d => (string?)d.Attribute("name") == "Home_Before_Text").Element("value")!.Value;

        Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Home_InterruptedRun_ShowsBanner()
    {
        var files = new InMemoryFileSystem();
        var store = TestViewModels.Store(files);
        store.Save(new RunState(@"C:\p.json", "hash", ["a"], [], DateTimeOffset.UnixEpoch));

        Assert.True(new HomeViewModel(store).HasInterruptedRun);
    }

    [Fact]
    public void Home_NoState_NoBanner()
    {
        var home = new HomeViewModel(TestViewModels.Store(new InMemoryFileSystem()));

        Assert.False(home.HasInterruptedRun);
    }
}
