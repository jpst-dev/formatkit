using System.Runtime.InteropServices;
using System.Security.Principal;

namespace FormatKit.Core.SystemAccess;

public sealed class WindowsUserSession : IUserSession
{
    private const uint NoSession = 0xFFFFFFFF;
    private const int WtsUserName = 5;
    private const int WtsDomainName = 7;

    public bool IsConsoleUser
    {
        get
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == NoSession)
            {
                return true;
            }

            var user = Query(sessionId, WtsUserName);
            var domain = Query(sessionId, WtsDomainName);
            if (string.IsNullOrEmpty(user))
            {
                return true;
            }

            var console = string.IsNullOrEmpty(domain) ? user : $"{domain}\\{user}";
            return string.Equals(WindowsIdentity.GetCurrent().Name, console, StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool HasInteractiveUser
    {
        get
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            return sessionId != NoSession && !string.IsNullOrEmpty(Query(sessionId, WtsUserName));
        }
    }

    public uint? ConsoleSessionId
    {
        get
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            return sessionId == NoSession ? null : sessionId;
        }
    }

    public string? CurrentUserSid
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value;
        }
    }

    private static string? Query(uint sessionId, int infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out _))
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out uint bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
