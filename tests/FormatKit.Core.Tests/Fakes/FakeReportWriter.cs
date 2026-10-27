using FormatKit.Core.Reporting;
using FormatKit.Core.Running;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeReportWriter : IReportWriter
{
    public const string Directory = @"C:\reports\fake";

    public List<RunSummary> Written { get; } = [];

    public string Write(RunSummary summary)
    {
        Written.Add(summary);
        return Directory;
    }
}
