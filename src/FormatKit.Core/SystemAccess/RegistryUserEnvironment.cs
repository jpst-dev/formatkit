using System.Runtime.InteropServices;

namespace FormatKit.Core.SystemAccess;

/// <param name="broadcastChange">Notifies running programs of the change; defaults to a WM_SETTINGCHANGE broadcast. Tests pass a no-op.</param>
public sealed class RegistryUserEnvironment(IRegistry registry, Action? broadcastChange = null) : IUserEnvironment
{
    private const string EnvironmentKey = @"HKCU\Environment";

    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint BroadcastTimeoutMs = 5000;
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    public string? Get(string name) => registry.Read(EnvironmentKey, name)?.Data;

    public void Set(string name, string value)
    {
        registry.Write(EnvironmentKey, name, new RegistryValue(RegistryKind.String, value));
        (broadcastChange ?? Broadcast)();
    }

    private static void Broadcast() =>
        SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, "Environment", SmtoAbortIfHung, BroadcastTimeoutMs, out _);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
