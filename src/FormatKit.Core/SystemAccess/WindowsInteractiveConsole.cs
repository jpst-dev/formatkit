using System.ComponentModel;
using System.Diagnostics;
using Serilog;

namespace FormatKit.Core.SystemAccess;

public sealed class WindowsInteractiveConsole(IUserSession session, ILogger log) : IInteractiveConsole
{
    private const int ErrorFileNotFound = 2;

    public bool CanPrompt => !Console.IsInputRedirected && session.HasInteractiveUser;

    public async Task<int> RunAttachedAsync(ProcessSpec spec, CancellationToken ct)
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

    public void WriteLine(string text) => Console.Out.WriteLine(text);

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
