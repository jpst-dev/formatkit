using FormatKit.Core.Localization;

namespace FormatKit.Core.WindowsUpdate;

public static class WindowsUpdateErrors
{
    public static string Describe(int hresult) => unchecked((uint)hresult) switch
    {
        0x8024402C => Strings.Get("WindowsUpdate_Error_CannotReach"),
        0x80070422 => Strings.Get("WindowsUpdate_Error_ServiceDisabled"),
        0x8024001E => Strings.Get("WindowsUpdate_Error_ServiceStopped"),
        _ => Strings.Format("WindowsUpdate_Error_Unknown", $"0x{hresult:X8}"),
    };
}
