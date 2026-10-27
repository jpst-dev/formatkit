namespace FormatKit.Core.SystemAccess;

public interface IElevation
{
    bool IsElevated { get; }

    /// <summary>Relaunches the current executable elevated. Returns its exit code, or null when the user declined UAC.</summary>
    Task<int?> RelaunchElevatedAsync(IReadOnlyList<string> args);
}
