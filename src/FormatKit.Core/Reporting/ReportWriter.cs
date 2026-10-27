using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FormatKit.Core.Localization;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Reporting;

public sealed class ReportWriter(IFileSystem files, AppPaths paths, TimeProvider clock) : IReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public string Write(RunSummary summary)
    {
        var stamp = clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var dir = Path.Combine(paths.ReportsDirectory, stamp);
        files.CreateDirectory(dir);
        files.WriteAllTextAtomic(Path.Combine(dir, "report.json"), JsonSerializer.Serialize(summary, Options));
        files.WriteAllTextAtomic(Path.Combine(dir, "report.md"), BuildMarkdown(summary));
        return dir;
    }

    private static string BuildMarkdown(RunSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {Strings.Get("Report_Title")}").AppendLine();
        sb.AppendLine($"**{Strings.Get("Report_Status")}:** {Strings.Get("Report_Status_" + summary.Status)}").AppendLine();

        if (!string.IsNullOrEmpty(summary.Error))
            sb.AppendLine(Cell(summary.Error)).AppendLine();

        sb.AppendLine($"| {Strings.Get("Report_Step")} | {Strings.Get("Report_Result")} | {Strings.Get("Report_Details")} |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var step in summary.Steps)
        {
            sb.AppendLine($"| {Cell(step.Title)} | {Strings.Get("Report_Outcome_" + step.Outcome)} | {Cell(string.Join("; ", step.Messages))} |");
        }

        var next = summary.Steps.Where(s => !string.IsNullOrWhiteSpace(s.NextAction)).ToList();
        if (next.Count > 0)
        {
            sb.AppendLine().AppendLine($"## {Strings.Get("Report_NextSteps")}").AppendLine();
            foreach (var step in next)
                sb.AppendLine($"- **{Cell(step.Title)}**: {step.NextAction!.ReplaceLineEndings(" ")}");
        }

        return sb.ToString();
    }

    private static string Cell(string text) =>
        text.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal);
}
