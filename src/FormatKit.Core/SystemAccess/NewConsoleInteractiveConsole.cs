using Serilog;

namespace FormatKit.Core.SystemAccess;

/// <summary>
/// The interactive console of a process that has none (the UI's worker): each attached process gets its own new
/// console window, where the user types; nothing typed passes through FormatKit.
/// </summary>
/// <remarks>
/// .NET has no CREATE_NEW_CONSOLE switch on <see cref="System.Diagnostics.ProcessStartInfo"/>. It is not needed here:
/// with <c>UseShellExecute = false</c> and <c>CreateNoWindow = false</c>, a console program started by a process that
/// has no console (the GUI-subsystem FormatKit.exe) gets a new console window from Windows. Started from a process
/// that has a console (a test host), it shares that console instead.
/// </remarks>
public sealed class NewConsoleInteractiveConsole(IUserSession session, ILogger log) : IInteractiveConsole
{
    public bool CanPrompt => session.HasInteractiveUser;

    public Task<int> RunAttachedAsync(ProcessSpec spec, CancellationToken ct)
    {
        var startInfo = AttachedProcess.CreateStartInfo(spec);
        startInfo.CreateNoWindow = false;
        return AttachedProcess.RunAsync(startInfo, spec, log, ct);
    }

    /// <summary>There is no console to write to; the text goes to the run log.</summary>
    public void WriteLine(string text) => log.Information("{Text}", text);
}
