using System.Globalization;
using FormatKit.Prepare.Rendering;

namespace FormatKit.Prepare.Tests.Rendering;

public class MarkdownRendererTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 14, 5, 59, TimeSpan.FromHours(1));

    private static PrepareItem Item(PrepareStatus status, PrepareGroup group, string title,
        string? detail = null, string? action = null) =>
        new("test." + title, group, status, title, detail, action);

    private static string[] Render(PrepareReport report, string culture)
    {
        var saved = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            return MarkdownRenderer.Render(report, "PC-JOAO", Now).Split(["\r\n", "\n"], StringSplitOptions.None);
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
    public void English_document_has_title_date_headings_checkboxes_and_summary()
    {
        var lines = Render(OnePerStatus(), "en-US");

        string[] expected =
        [
            "# FormatKit preparation checklist — PC-JOAO", "",
            "Date: 2026-10-29 14:05", "",
            "## Blocker", "",
            "### Developer", "",
            "- [ ] B", "  b detail", "  b action", "",
            "## Attention", "",
            "### Security and licenses", "",
            "- [ ] A", "  a detail", "",
            "## Not checked", "",
            "### Personal data", "",
            "- N", "",
            "## Ok", "",
            "### Apps and FormatKit", "",
            "- O", "  o action", "",
            "1 item blocks formatting.", "",
        ];
        Assert.Equal(expected, lines);
    }

    [Fact]
    public void Portuguese_document_is_localized()
    {
        var lines = Render(OnePerStatus(), "pt-BR");

        Assert.Equal("# Checklist de preparação do FormatKit — PC-JOAO", lines[0]);
        Assert.Contains("## Bloqueio", lines);
        Assert.Contains("## Não verificado", lines);
        Assert.Contains("### Aplicativos e FormatKit", lines);
        Assert.Equal("1 item impede a formatação.", lines[^2]);
    }

    [Fact]
    public void Summary_when_safe_and_with_two_blockers()
    {
        var safe = Render(new PrepareReport([Item(PrepareStatus.Ok, PrepareGroup.Data, "O")]), "en-US");
        var two = Render(new PrepareReport(
        [
            Item(PrepareStatus.Blocker, PrepareGroup.Data, "B1"),
            Item(PrepareStatus.Blocker, PrepareGroup.Data, "B2"),
        ]), "en-US");

        Assert.Equal("Safe to format.", safe[^2]);
        Assert.Equal("No blockers. Review the item marked Attention before formatting.",
            Render(new PrepareReport([Item(PrepareStatus.Attention, PrepareGroup.Data, "A")]), "en-US")[^2]);
        Assert.Equal("2 items block formatting.", two[^2]);
    }

    [Fact]
    public void Date_uses_the_given_instant_in_its_own_offset()
    {
        var lines = Render(new PrepareReport([]), "pt-BR");

        Assert.Contains("Data: 2026-10-29 14:05", lines);
    }

    [Fact]
    public void Only_a_leading_hash_or_dash_in_a_title_is_escaped()
    {
        var report = new PrepareReport(
        [
            Item(PrepareStatus.Ok, PrepareGroup.Data, "# one"),
            Item(PrepareStatus.Ok, PrepareGroup.Data, "- two"),
            Item(PrepareStatus.Ok, PrepareGroup.Data, @"D:\a-b #c [x]*y_z"),
        ]);

        var lines = Render(report, "en-US");

        Assert.Contains(@"- \# one", lines);
        Assert.Contains(@"- \- two", lines);
        Assert.Contains(@"- D:\a-b #c [x]*y_z", lines);
    }

    [Fact]
    public void Detail_and_action_are_printed_unchanged_and_multiline_text_stays_indented()
    {
        var report = new PrepareReport(
        [
            Item(PrepareStatus.Attention, PrepareGroup.Data, "T", @"# E:\- x\y", "line1\nline2"),
        ]);

        var lines = Render(report, "en-US");

        Assert.Contains(@"  # E:\\- x\y", lines);
        Assert.Contains("  line1", lines);
        Assert.Contains("  line2", lines);
    }

    [Fact]
    public void A_backslash_before_punctuation_or_at_the_end_is_doubled_so_the_path_renders_unchanged()
    {
        // CommonMark reads \. \_ \[ as escapes and a trailing \ as a line break; the path must survive rendering.
        var report = new PrepareReport(
        [
            Item(PrepareStatus.Attention, PrepareGroup.Developer, @"C:\Users\joao\.ssh", @"C:\Users\joao\_work\[x]", @"Copy C:\"),
            Item(PrepareStatus.Ok, PrepareGroup.Data, @"\\srv\share\-x"),
        ]);

        var lines = Render(report, "en-US");

        Assert.Contains(@"- [ ] C:\Users\joao\\.ssh", lines);
        Assert.Contains(@"  C:\Users\joao\\_work\\[x]", lines);
        Assert.Contains(@"  Copy C:\\", lines);
        Assert.Contains(@"- \\\srv\share\\-x", lines);
    }
}
