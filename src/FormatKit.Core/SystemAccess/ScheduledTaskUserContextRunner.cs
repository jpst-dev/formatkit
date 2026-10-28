using System.Diagnostics;
using System.Globalization;

namespace FormatKit.Core.SystemAccess;

/// <summary>Runs a process as the interactive user through a temporary, limited-run-level scheduled task.</summary>
public sealed class ScheduledTaskUserContextRunner(IProcessRunner runner) : IUserContextRunner
{
    private static readonly TimeSpan OuterSlack = TimeSpan.FromMinutes(2);

    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct)
    {
        var script = BuildScript(spec);
        var stopwatch = Stopwatch.StartNew();
        var result = await runner.RunAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], spec.Timeout + OuterSlack),
            ct).ConfigureAwait(false);
        stopwatch.Stop();

        var last = result.StdOut
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();

        if (last == "TIMEOUT")
        {
            return new ProcessResult(-1, string.Empty, string.Empty, stopwatch.Elapsed, TimedOut: true);
        }

        if (last is not null && int.TryParse(last, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exitCode))
        {
            return new ProcessResult(exitCode, string.Empty, string.Empty, stopwatch.Elapsed, TimedOut: false);
        }

        throw new InvalidOperationException(
            $"User-context task printed unexpected output '{last}' (exit {result.ExitCode}): {result.StdErr.Trim()}");
    }

    internal static string BuildScript(ProcessSpec spec)
    {
        var taskName = $"FormatKit-User-{Guid.NewGuid():N}";
        var arguments = string.Join(' ', spec.Arguments.Select(QuoteArgument));
        var timeoutSeconds = (int)Math.Ceiling(spec.Timeout.TotalSeconds);
        var body = string.Join(
            "; ",
            $"$a=New-ScheduledTaskAction -Execute {Quote(spec.FileName)} -Argument {Quote(arguments)}",
            "$p=New-ScheduledTaskPrincipal -UserId (Get-CimInstance Win32_ComputerSystem).UserName -LogonType Interactive -RunLevel Limited",
            "Register-ScheduledTask -TaskName $n -Action $a -Principal $p | Out-Null",
            "Start-ScheduledTask -TaskName $n",
            "$sw=[Diagnostics.Stopwatch]::StartNew()",
            "$timedOut=$false",
            $"while((Get-ScheduledTask -TaskName $n).State -eq 'Running'){{ if($sw.Elapsed.TotalSeconds -ge {timeoutSeconds}){{ $timedOut=$true; break }}; Start-Sleep -Seconds 2 }}",
            "if($timedOut){ 'TIMEOUT' } else { [string][int](Get-ScheduledTaskInfo -TaskName $n).LastTaskResult }");
        return string.Join(
            "; ",
            "$ErrorActionPreference='Stop'",
            $"$n={Quote(taskName)}",
            "try{ " + body + " }",
            "finally{ Unregister-ScheduledTask -TaskName $n -Confirm:$false -ErrorAction SilentlyContinue }");
    }

    // Windows command-line quoting for the task's Argument string.
    private static string QuoteArgument(string argument) =>
        argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0
            ? argument
            : "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
