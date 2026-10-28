namespace FormatKit.Core.SystemAccess;

public interface IUserContextRunner
{
    /// <summary>
    /// Runs the process as the signed-in user (non-elevated), waits for it to finish and returns its exit code.
    /// StdOut and StdErr are empty because the child's output is not captured.
    /// </summary>
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct);
}
