using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Rendering;

/// <summary>
/// Prints the report to the console: a heading per status, a sub-heading per group (indented, ending with ":"), then
/// the items one level deeper with their detail and action below them.
/// </summary>
public static class ConsoleRenderer
{
    public static void Write(TextWriter output, PrepareReport report)
    {
        foreach (var status in report.Items.Select(i => i.Status).Distinct().Order())
        {
            output.WriteLine(StatusTitle(status));
            foreach (var group in report.Items.Where(i => i.Status == status).Select(i => i.Group).Distinct().Order())
            {
                output.WriteLine("  " + GroupTitle(group) + ":");
                foreach (var item in report.Items.Where(i => i.Status == status && i.Group == group))
                {
                    output.WriteLine("    " + item.Title);
                    WriteIndented(output, item.Detail);
                    WriteIndented(output, item.Action);
                }
            }
            output.WriteLine();
        }
        output.WriteLine(Summary(report));
    }

    public static string StatusTitle(PrepareStatus status) => Strings.Get("Status_" + status);

    public static string GroupTitle(PrepareGroup group) => Strings.Get("Group_" + group);

    /// <summary>Blockers first; without them, Attention items are named so "safe" never sits under a warning.</summary>
    public static string Summary(PrepareReport report)
    {
        var blockers = report.Items.Count(i => i.Status == PrepareStatus.Blocker);
        var attention = report.Items.Count(i => i.Status == PrepareStatus.Attention);
        return (blockers, attention) switch
        {
            (1, _) => Strings.Get("Summary_Blocked_One"),
            ( > 1, _) => Strings.Format("Summary_Blocked_Many", blockers),
            (0, 1) => Strings.Get("Summary_Review_One"),
            (0, > 1) => Strings.Format("Summary_Review_Many", attention),
            _ => Strings.Get("Summary_Safe"),
        };
    }

    private static void WriteIndented(TextWriter output, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
            output.WriteLine("      " + line);
    }
}
