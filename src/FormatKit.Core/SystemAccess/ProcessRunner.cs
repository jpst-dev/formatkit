using System.ComponentModel;
using System.Diagnostics;
using Serilog;

namespace FormatKit.Core.SystemAccess;

public sealed class ProcessRunner(ILogger log) : IProcessRunner
{
    private const int ErrorFileNotFound = 2;
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(spec.FileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in spec.Environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
        }

        var commandLine = $"{spec.FileName} {string.Join(' ', spec.Arguments)}".TrimEnd();
        log.Debug("Starting process: {CommandLine}", commandLine);

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorFileNotFound)
        {
            throw new FileNotFoundException($"Executable not found: {spec.FileName}", spec.FileName, ex);
        }

        var stdOutTask = ProcessOutputDecoder.ReadAsync(process.StandardOutput.BaseStream);
        var stdErrTask = ProcessOutputDecoder.ReadAsync(process.StandardError.BaseStream);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(spec.Timeout);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            timedOut = true;
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        var stdOut = await DrainAsync(stdOutTask, "stdout", commandLine, ct).ConfigureAwait(false);
        var stdErr = await DrainAsync(stdErrTask, "stderr", commandLine, ct).ConfigureAwait(false);
        stopwatch.Stop();

        var exitCode = process.ExitCode;
        log.Debug(
            "Process finished: {CommandLine} exit={ExitCode} timedOut={TimedOut} duration={Duration} stdout={StdOut} stderr={StdErr}",
            commandLine, exitCode, timedOut, stopwatch.Elapsed, stdOut, stdErr);
        log.Information(
            "{FileName} exited with {ExitCode} in {Seconds:F1}s{TimedOutNote}",
            spec.FileName, exitCode, stopwatch.Elapsed.TotalSeconds, timedOut ? " (timed out)" : string.Empty);

        return new ProcessResult(exitCode, stdOut, stdErr, stopwatch.Elapsed, timedOut);
    }

    /// <summary>Waits a bounded time for a redirected stream: a grandchild can keep the pipe open.</summary>
    private async Task<string> DrainAsync(Task<string> read, string stream, string commandLine, CancellationToken ct)
    {
        try
        {
            return await read.WaitAsync(DrainTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            log.Warning("Timed out reading {Stream} of {CommandLine}; returning what was captured", stream, commandLine);
            return string.Empty;
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}
