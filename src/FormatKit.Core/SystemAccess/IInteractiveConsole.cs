namespace FormatKit.Core.SystemAccess;

public interface IInteractiveConsole
{
    /// <summary>True when a person can answer a prompt: input is not redirected and a user is signed in at the console.</summary>
    bool CanPrompt { get; }

    /// <summary>Runs a process attached to this console (no redirection) and returns its exit code. Kills it when <see cref="ProcessSpec.Timeout"/> elapses.</summary>
    Task<int> RunAttachedAsync(ProcessSpec spec, CancellationToken ct);

    void WriteLine(string text);
}
