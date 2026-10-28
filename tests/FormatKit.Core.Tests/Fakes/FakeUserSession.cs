using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.Fakes;

public sealed class FakeUserSession : IUserSession
{
    public bool IsConsoleUser { get; set; } = true;

    public bool HasInteractiveUser { get; set; } = true;

    public string? CurrentUserSid { get; set; } = "S-1-5-21-1000-1000-1000-1001";
}
