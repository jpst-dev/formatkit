using System.Xml.Linq;

namespace FormatKit.App.Tests;

/// <summary>
/// UI Automation names a list item after its item's ToString unless the item container sets
/// AutomationProperties.Name, so screen readers read "NavItem { Page = Home, ... }" (seen in VM scenario 1). Every
/// list over records must name its items; lists of plain strings already read well.
/// </summary>
public sealed class AccessibleNamesTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>Lists whose items are strings, as "File:ItemsSource path".</summary>
    private static readonly HashSet<string> StringLists =
    [
        "CatalogsPage.xaml:Details.Lines",
        "HardwarePage.xaml:GpuLines",
        "HardwarePage.xaml:AdapterLines",
        "HardwarePage.xaml:ProblemLines",
        "HardwarePage.xaml:UpdateLines",
        "HardwarePage.xaml:GpuPackageLines",
        "RunProfilePage.xaml:Items",
        "RunProfilePage.xaml:Messages",
    ];

    private static readonly string[] ListElements = ["ItemsControl", "ListBox", "ComboBox"];

    private static DirectoryInfo AppSource()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "src", "FormatKit.App", "MainWindow.xaml")))
            root = root.Parent ?? throw new InvalidOperationException("repository root not found");
        return new DirectoryInfo(Path.Combine(root.FullName, "src", "FormatKit.App"));
    }

    private static string? SourcePath(XElement list)
    {
        var source = (string?)list.Attribute("ItemsSource");
        if (source is null) return null;
        // "{Binding Path}" or "{Binding Path, ...}"
        return source.Replace("{Binding", "", StringComparison.Ordinal).Trim(' ', '}').Split(',')[0].Trim();
    }

    private static bool NamesItsItems(XElement list) =>
        list.Elements(Wpf + (list.Name.LocalName + ".ItemContainerStyle"))
            .Descendants(Wpf + "Setter")
            .Any(s => (string?)s.Attribute("Property") == "AutomationProperties.Name");

    [Fact]
    public void EveryListOverRecords_NamesItsItems()
    {
        var missing = new List<string>();
        foreach (var file in AppSource().EnumerateFiles("*.xaml", SearchOption.AllDirectories)
                     .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var doc = XDocument.Load(file.FullName);
            foreach (var list in doc.Descendants().Where(e => e.Name.Namespace == Wpf && ListElements.Contains(e.Name.LocalName)))
            {
                var path = SourcePath(list);
                if (path is null || StringLists.Contains($"{file.Name}:{path}")) continue;
                if (!NamesItsItems(list))
                    missing.Add($"{file.Name}:{path}");
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void AboutRepositoryLink_HasAName()
    {
        var doc = XDocument.Load(Path.Combine(AppSource().FullName, "Views", "AboutPage.xaml"));
        var link = Assert.Single(doc.Descendants(Wpf + "Hyperlink"));

        Assert.NotNull(link.Attribute("AutomationProperties.Name"));
    }
}
