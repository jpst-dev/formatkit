using System.Diagnostics;
using System.Globalization;
using System.Text;

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

        if (result.TimedOut)
        {
            return new ProcessResult(-1, string.Empty, string.Empty, stopwatch.Elapsed, TimedOut: true);
        }

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
            "$s=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0",
            "Register-ScheduledTask -TaskName $n -Action $a -Principal $p -Settings $s | Out-Null",
            "Start-ScheduledTask -TaskName $n",
            "$sw=[Diagnostics.Stopwatch]::StartNew()",
            "$timedOut=$false; $seen=$false",
            // 267011 = never run yet, 267009 = running: until the task was seen Running or has a real result, keep polling.
            $"while($true){{ $st=(Get-ScheduledTask -TaskName $n).State; $r=(Get-ScheduledTaskInfo -TaskName $n).LastTaskResult; if($st -eq 'Running'){{ $seen=$true }} elseif($seen -or ($r -ne 267011 -and $r -ne 267009)){{ break }}; if($sw.Elapsed.TotalSeconds -ge {timeoutSeconds}){{ $timedOut=$true; break }}; Start-Sleep -Seconds 2 }}",
            "if($timedOut){ 'TIMEOUT' } else { $info=Get-ScheduledTaskInfo -TaskName $n; [string][BitConverter]::ToInt32([BitConverter]::GetBytes([uint32]$info.LastTaskResult),0) }");
        return string.Join(
            "; ",
            "$ErrorActionPreference='Stop'",
            $"$n={Quote(taskName)}",
            "try{ " + body + " }",
            "finally{ Stop-ScheduledTask -TaskName $n -ErrorAction SilentlyContinue; Unregister-ScheduledTask -TaskName $n -Confirm:$false -ErrorAction SilentlyContinue }");
    }

    // Standard Windows command-line quoting (CommandLineToArgvW rules).
    internal static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return argument;
        }

        var sb = new StringBuilder("\"");
        for (var i = 0; i < argument.Length; i++)
        {
            var backslashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                backslashes++;
                i++;
            }

            if (i == argument.Length)
            {
                sb.Append('\\', backslashes * 2);
            }
            else if (argument[i] == '"')
            {
                sb.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(argument[i]);
            }
        }

        return sb.Append('"').ToString();
    }

    internal static string Quote(string value)
    {
        var sb = new StringBuilder("'");
        foreach (var c in value)
        {
            sb.Append(c);
            if (c is '\'' or '‘' or '’' or '‚' or '‛')
            {
                sb.Append(c);
            }
        }

        return sb.Append('\'').ToString();
    }
}
