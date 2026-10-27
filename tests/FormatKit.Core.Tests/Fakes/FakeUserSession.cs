using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeUserSession : IUserSession
{
    public bool IsConsoleUser { get; set; } = true;
}
