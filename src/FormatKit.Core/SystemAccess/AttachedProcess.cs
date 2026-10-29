using System.ComponentModel;
using System.Diagnostics;
using Serilog;

namespace FormatKit.Core.SystemAccess;

/// <summary>
/// What the interactive consoles share: the start info of an unredirected process (arguments as a list, extra
/// environment) and the run itself (file-name-only logging, timeout kill, <see cref="FileNotFoundException"/> mapping).
/// </summary>
internal static class AttachedProcess
{
    private const int ErrorFileNotFound = 2;

    public static ProcessStartInfo CreateStartInfo(ProcessSpec spec)
    {
        var startInfo = new ProcessStartInfo(spec.FileName) { UseShellExecute = false };
        foreach (var argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in spec.Environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
        }

        return startInfo;
    }

    /// <summary>Starts the process, waits for it and returns its exit code; kills its tree when the timeout elapses.</summary>
    public static async Task<int> RunAsync(ProcessStartInfo startInfo, ProcessSpec spec, ILogger log, CancellationToken ct)
    {
        // Arguments may carry user input, so only the file name is logged.
        log.Information("Starting attached process: {FileName}", spec.FileName);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorFileNotFound)
        {
            throw new FileNotFoundException($"Executable not found: {spec.FileName}", spec.FileName, ex);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(spec.Timeout);
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

            log.Warning("{FileName} timed out after {Timeout}; killed", spec.FileName, spec.Timeout);
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        log.Information("{FileName} exited with {ExitCode}", spec.FileName, process.ExitCode);
        return process.ExitCode;
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
