using System.Globalization;
using FormatKit.Prepare.Rendering;

namespace FormatKit.Prepare.Tests.Rendering;

public class ConsoleRendererTests
{
    private static PrepareItem Item(PrepareStatus status, PrepareGroup group, string title,
        string? detail = null, string? action = null) =>
        new("test." + title, group, status, title, detail, action);

    private static string[] Render(PrepareReport report, string culture)
    {
        var saved = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            var writer = new StringWriter();
            ConsoleRenderer.Write(writer, report);
            return writer.ToString().Split(["\r\n", "\n"], StringSplitOptions.None);
        }
        finally { CultureInfo.CurrentUICulture = saved; }
    }

    private static PrepareReport OnePerStatus() => new(
    [
        Item(PrepareStatus.Blocker, PrepareGroup.Developer, "B", "b detail", "b action"),
        Item(PrepareStatus.Attention, PrepareGroup.Security, "A", "a detail"),
        Item(PrepareStatus.NotChecked, PrepareGroup.Data, "N"),
        Item(PrepareStatus.Ok, PrepareGroup.Apps, "O", null, "o action"),
    ]);

    [Fact]
    public void English_prints_headings_groups_and_indented_text_in_order()
    {
        var lines = Render(OnePerStatus(), "en-US");

        string[] expected =
        [
            "Blocker", "  Developer", "  B", "    b detail", "    b action", "",
            "Attention", "  Security and licenses", "  A", "    a detail", "",
            "Not checked", "  Personal data", "  N", "",
            "Ok", "  Apps and FormatKit", "  O", "    o action", "",
            "1 item blocks formatting.", "",
        ];
        Assert.Equal(expected, lines);
    }

    [Fact]
    public void Portuguese_prints_localized_headings_and_summary()
    {
        var lines = Render(OnePerStatus(), "pt-BR");

        Assert.Equal(["Bloqueio", "Atenção", "Não verificado", "Ok"],
            lines.Where(l => l.Length > 0 && l[0] != ' ' && !l.Contains("impede")));
        Assert.Contains("  Segurança e licenças", lines);
        Assert.Contains("  Dados pessoais", lines);
        Assert.Contains("  Desenvolvimento", lines);
        Assert.Contains("  Aplicativos e FormatKit", lines);
        Assert.Equal("1 item impede a formatação.", lines[^2]);
    }

    [Fact]
    public void Empty_statuses_print_no_heading()
    {
        var report = new PrepareReport([Item(PrepareStatus.Ok, PrepareGroup.Data, "O")]);

        var lines = Render(report, "en-US");

        Assert.DoesNotContain("Blocker", lines);
        Assert.DoesNotContain("Attention", lines);
        Assert.DoesNotContain("Not checked", lines);
        Assert.Equal(["Ok", "  Personal data", "  O", "", "Safe to format.", ""], lines);
    }

    [Fact]
    public void Group_heading_is_printed_once_per_group_within_a_status()
    {
        var report = new PrepareReport(
        [
            Item(PrepareStatus.Attention, PrepareGroup.Security, "A1"),
            Item(PrepareStatus.Attention, PrepareGroup.Security, "A2"),
            Item(PrepareStatus.Attention, PrepareGroup.Data, "A3"),
        ]);

        var lines = Render(report, "en-US");

        Assert.Single(lines, "  Security and licenses");
        Assert.Equal(["Attention", "  Security and licenses", "  A1", "  A2", "  Personal data", "  A3", "",
            "Safe to format.", ""], lines);
    }

    [Theory]
    [InlineData("en-US", "Safe to format.", "2 items block formatting.")]
    [InlineData("pt-BR", "Seguro formatar.", "2 itens impedem a formatação.")]
    public void Last_line_is_the_summary(string culture, string safe, string blocked)
    {
        var none = Render(new PrepareReport([Item(PrepareStatus.Ok, PrepareGroup.Data, "O")]), culture);
        var two = Render(new PrepareReport(
        [
            Item(PrepareStatus.Blocker, PrepareGroup.Data, "B1"),
            Item(PrepareStatus.Blocker, PrepareGroup.Data, "B2"),
        ]), culture);

        Assert.Equal(safe, none[^2]);
        Assert.Equal(blocked, two[^2]);
    }

    [Fact]
    public void Item_text_is_printed_unchanged()
    {
        var path = @"# D:\- repos\[a]*b `c` _d_";
        var report = new PrepareReport([Item(PrepareStatus.Ok, PrepareGroup.Data, path, path, path)]);

        var lines = Render(report, "en-US");

        Assert.Contains("  " + path, lines);
        Assert.Contains("    " + path, lines);
    }
}
