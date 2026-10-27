using System.Globalization;
using System.Text.Json;
using FormatKit.Core.Localization;
using FormatKit.Core.Reporting;
using FormatKit.Core.Running;
using FormatKit.Core.Steps;
using FormatKit.Core.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace FormatKit.Core.Tests.Reporting;

[Collection("Culture")]
public class ReportWriterTests : IDisposable
{
    private readonly CultureInfo _original = CultureInfo.CurrentUICulture;
    private readonly InMemoryFileSystem _fs = new();
    private readonly AppPaths _paths = new(Path.Combine("data", "FormatKit"));
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 27, 14, 3, 5, TimeSpan.Zero));

    public ReportWriterTests() => CultureInfo.CurrentUICulture = new CultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentUICulture = _original;

    private static RunSummary Summary() => new(
        RunStatus.CompletedWithFailures,
        [
            new StepReport("net", "Check network", StepOutcome.Succeeded, ["Online"], null),
            new StepReport("gpu", "Install GPU driver", StepOutcome.Failed, ["Download failed"], "Install the driver manually"),
        ],
        null,
        null);

    private string Dir => Path.Combine(_paths.ReportsDirectory, "20261027-140305");

    [Fact]
    public void Writes_json_and_markdown_in_timestamped_folder()
    {
        var dir = new ReportWriter(_fs, _paths, _clock).Write(Summary());

        Assert.Equal(Dir, dir);
        Assert.True(_fs.FileExists(Path.Combine(dir, "report.json")));
        Assert.True(_fs.FileExists(Path.Combine(dir, "report.md")));
    }

    [Fact]
    public void Json_contains_status_and_every_step()
    {
        var summary = Summary();
        new ReportWriter(_fs, _paths, _clock).Write(summary);

        using var doc = JsonDocument.Parse(_fs.ReadAllText(Path.Combine(Dir, "report.json")));
        var root = doc.RootElement;
        Assert.Equal("completedWithFailures", root.GetProperty("status").GetString());
        var steps = root.GetProperty("steps");
        Assert.Equal(summary.Steps.Count, steps.GetArrayLength());
        Assert.Equal("failed", steps[1].GetProperty("outcome").GetString());
        Assert.Equal("Install the driver manually", steps[1].GetProperty("nextAction").GetString());
    }

    [Fact]
    public void Markdown_lists_failed_steps_with_next_action()
    {
        new ReportWriter(_fs, _paths, _clock).Write(Summary());

        var md = _fs.ReadAllText(Path.Combine(Dir, "report.md"));
        Assert.Contains("Install GPU driver", md);
        Assert.Contains("Install the driver manually", md);
    }

    [Fact]
    public void Markdown_uses_current_ui_culture()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");
        new ReportWriter(_fs, _paths, _clock).Write(Summary());

        var md = _fs.ReadAllText(Path.Combine(Dir, "report.md"));
        Assert.Contains(Strings.Get("Report_Title"), md);
        Assert.Contains("# Relatório de execução do FormatKit", md);
        Assert.DoesNotContain("# FormatKit run report", md);
    }
}
