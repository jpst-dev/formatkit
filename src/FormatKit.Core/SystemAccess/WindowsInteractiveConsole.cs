using Serilog;

namespace FormatKit.Core.SystemAccess;

public sealed class WindowsInteractiveConsole(IUserSession session, ILogger log) : IInteractiveConsole
{
    public bool CanPrompt => !Console.IsInputRedirected && session.HasInteractiveUser;

    public Task<int> RunAttachedAsync(ProcessSpec spec, CancellationToken ct) =>
        AttachedProcess.RunAsync(AttachedProcess.CreateStartInfo(spec), spec, log, ct);

    public void WriteLine(string text) => Console.Out.WriteLine(text);
}
