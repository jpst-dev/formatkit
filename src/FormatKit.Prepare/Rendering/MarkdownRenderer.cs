using System.Globalization;
using System.Text;
using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Rendering;

/// <summary>Renders the report as a Markdown checklist in the UI language.</summary>
public static class MarkdownRenderer
{
    public static string Render(PrepareReport report, string computerName, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# " + Strings.Format("Markdown_Title", computerName)).AppendLine();
        sb.AppendLine(Strings.Format("Markdown_Date", now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)))
            .AppendLine();

        foreach (var status in report.Items.Select(i => i.Status).Distinct().Order())
        {
            sb.AppendLine("## " + Strings.Get("Status_" + status)).AppendLine();
            var box = status is PrepareStatus.Blocker or PrepareStatus.Attention ? "- [ ] " : "- ";
            foreach (var group in report.Items.Where(i => i.Status == status).Select(i => i.Group).Distinct().Order())
            {
                sb.AppendLine("### " + Strings.Get("Group_" + group)).AppendLine();
                foreach (var item in report.Items.Where(i => i.Status == status && i.Group == group))
                {
                    sb.AppendLine(box + EscapeLeading(EscapeBackslashes(item.Title)));
                    AppendIndented(sb, item.Detail);
                    AppendIndented(sb, item.Action);
                }
                sb.AppendLine();
            }
        }
        sb.AppendLine(ConsoleRenderer.Summary(report));
        return sb.ToString();
    }

    /// <summary>Only a leading # or - would change the meaning of the line; anything else is kept as is.</summary>
    private static string EscapeLeading(string title) =>
        title.StartsWith('#') || title.StartsWith('-') ? "\\" + title : title;

    /// <summary>
    /// CommonMark reads a backslash before ASCII punctuation as an escape and one at the end of a line as a line break,
    /// so <c>C:\Users\joao\.ssh</c> would lose a backslash. Doubling exactly those keeps every path as it is.
    /// </summary>
    private static string EscapeBackslashes(string line)
    {
        var sb = new StringBuilder(line.Length);
        for (var i = 0; i < line.Length; i++)
        {
            sb.Append(line[i]);
            if (line[i] == '\\' && (i + 1 == line.Length || IsAsciiPunctuation(line[i + 1])))
                sb.Append('\\');
        }

        return sb.ToString();
    }

    private static bool IsAsciiPunctuation(char c) =>
        c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';

    private static void AppendIndented(StringBuilder sb, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
            sb.AppendLine("  " + EscapeBackslashes(line));
    }
}
