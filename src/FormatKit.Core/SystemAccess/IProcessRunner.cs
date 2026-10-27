namespace FormatKit.Core.SystemAccess;

public sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout);

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, TimeSpan Duration, bool TimedOut);

public interface IProcessRunner
{
    /// <summary>Runs a process to completion or timeout. Throws <see cref="FileNotFoundException"/> when the executable does not exist.</summary>
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct);
}
