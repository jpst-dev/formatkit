using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Rendering;

/// <summary>Prints the report to the console: a heading per status, a sub-heading per group, then the items.</summary>
public static class ConsoleRenderer
{
    public static void Write(TextWriter output, PrepareReport report)
    {
        foreach (var status in report.Items.Select(i => i.Status).Distinct().Order())
        {
            output.WriteLine(Strings.Get("Status_" + status));
            foreach (var group in report.Items.Where(i => i.Status == status).Select(i => i.Group).Distinct().Order())
            {
                output.WriteLine("  " + Strings.Get("Group_" + group));
                foreach (var item in report.Items.Where(i => i.Status == status && i.Group == group))
                {
                    output.WriteLine("  " + item.Title);
                    WriteIndented(output, item.Detail);
                    WriteIndented(output, item.Action);
                }
            }
            output.WriteLine();
        }
        output.WriteLine(Summary(report));
    }

    internal static string Summary(PrepareReport report)
    {
        var blockers = report.Items.Count(i => i.Status == PrepareStatus.Blocker);
        return blockers switch
        {
            0 => Strings.Get("Summary_Safe"),
            1 => Strings.Get("Summary_Blocked_One"),
            _ => Strings.Format("Summary_Blocked_Many", blockers),
        };
    }

    private static void WriteIndented(TextWriter output, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
            output.WriteLine("    " + line);
    }
}
