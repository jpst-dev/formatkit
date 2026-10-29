using FormatKit.Core.SystemAccess;

namespace FormatKit.App.Worker;

/// <summary>
/// The worker's interactive console (spec 6.5): runs and logs through <paramref name="inner"/> and tells the window
/// about it, so the running step can say "if the console window that opened asks for something, type it there"
/// while the console is open (also one that normally asks nothing, such as the dotfiles clone) and show the lines a
/// step writes. Only that is sent: never the process's arguments (a remote URL) and never what the user types, which
/// stays in the console window.
/// </summary>
public sealed class PipeInteractiveConsole(IInteractiveConsole inner, MessageChannel channel) : IInteractiveConsole
{
    public bool CanPrompt => inner.CanPrompt;

    public async Task<int> RunAttachedAsync(ProcessSpec spec, CancellationToken ct)
    {
        channel.Post(new ConsoleOpened());
        try
        {
            return await inner.RunAttachedAsync(spec, ct).ConfigureAwait(false);
        }
        finally
        {
            channel.Post(new ConsoleClosed());
        }
    }

    public void WriteLine(string text)
    {
        inner.WriteLine(text);
        channel.Post(new ConsoleLine(text));
    }
}
