using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FormatKit.Prepare.Tests;

public class ResourcesTests
{
    private static Dictionary<string, string> Load(string file)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "FormatKit.sln"))) dir = Path.GetDirectoryName(dir);
        var path = Path.Combine(dir!, "src", "FormatKit.Prepare", "Resources", file);
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);
    }

    [Fact]
    public void BothFilesHaveTheSameNamesAndPlaceholders()
    {
        var en = Load("Strings.resx");
        var pt = Load("Strings.pt-BR.resx");
        Assert.Empty(en.Keys.Except(pt.Keys));
        Assert.Empty(pt.Keys.Except(en.Keys));
        foreach (var (name, value) in en)
            Assert.Equal(Placeholders(value), Placeholders(pt[name]));
    }

    private static string Placeholders(string text) =>
        string.Join(",", Regex.Matches(text, @"\{(\d+)").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));
}
