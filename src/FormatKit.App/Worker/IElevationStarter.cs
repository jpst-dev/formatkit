namespace FormatKit.App.Worker;

/// <summary>Starts a process, elevated through the UAC prompt when asked.</summary>
public interface IElevationStarter
{
    /// <summary>Returns null when the user declined the UAC prompt.</summary>
    Task<IStartedProcess?> StartAsync(string exe, IReadOnlyList<string> args, bool runas);
}

public interface IStartedProcess
{
    /// <summary>Completes with the exit code when the process exits.</summary>
    Task<int> Exited { get; }
}
