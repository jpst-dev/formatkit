namespace FormatKit.Core.SystemAccess;

public interface IUserSession
{
    /// <summary>True when the current process runs as the user logged on at the physical console (or no console user can be determined).</summary>
    bool IsConsoleUser { get; }

    /// <summary>The SID of the user the current process runs as, or null when it cannot be determined.</summary>
    string? CurrentUserSid { get; }
}
