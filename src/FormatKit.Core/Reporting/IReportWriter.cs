using FormatKit.Core.Running;

namespace FormatKit.Core.Reporting;

public interface IReportWriter
{
    /// <summary>Writes the report and returns the report directory.</summary>
    string Write(RunSummary summary);
}
