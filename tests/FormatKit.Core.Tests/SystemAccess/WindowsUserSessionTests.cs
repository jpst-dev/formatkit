using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.SystemAccess;

public sealed class WindowsUserSessionTests
{
    private const uint NoConsole = 0xFFFFFFFF;

    [Theory]
    [InlineData(2u, 1u, true)] // Remote Desktop or an enhanced session: the case the end-to-end VM run hit.
    [InlineData(1u, 1u, false)] // At the console.
    [InlineData(0u, 1u, false)] // A service: not a session anyone is looking at.
    [InlineData(2u, NoConsole, false)] // During a session switch: unknown, so no warning.
    public void IsOutsideConsole_compares_the_process_session_with_the_console(uint process, uint console, bool expected) =>
        Assert.Equal(expected, WindowsUserSession.IsOutsideConsole(process, console));
}
