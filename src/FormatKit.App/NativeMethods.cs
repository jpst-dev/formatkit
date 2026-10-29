using System.Runtime.InteropServices;

namespace FormatKit.App;

internal static class NativeMethods
{
    /// <summary>ASFW_ANY: any process may take the foreground.</summary>
    public const int AsfwAny = -1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int processId);
}
