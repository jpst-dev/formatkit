namespace FormatKit.Core.SystemAccess;

public interface IUserSession
{
    /// <summary>True when the current process runs as the user logged on at the physical console (or no console user can be determined).</summary>
    bool IsConsoleUser { get; }

    /// <summary>The SID of the user the current process runs as, or null when it cannot be determined.</summary>
    string? CurrentUserSid { get; }

    /// <summary>True when the WTS console session has a signed-in user.</summary>
    bool HasInteractiveUser { get; }

    /// <summary>The WTS session id of the physical console, or null when there is none (for example during a session switch).</summary>
    uint? ConsoleSessionId => null;
}
