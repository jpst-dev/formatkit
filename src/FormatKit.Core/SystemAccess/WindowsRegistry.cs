using System.Globalization;
using Microsoft.Win32;

namespace FormatKit.Core.SystemAccess;

public sealed class WindowsRegistry : IRegistry
{
    public RegistryValue? Read(string path, string name)
    {
        var (hive, subKey) = Split(path);
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(subKey, writable: false);
        if (key is null)
        {
            return null;
        }

        var raw = key.GetValue(name);
        if (raw is null)
        {
            return null;
        }

        var kind = key.GetValueKind(name);
        return kind switch
        {
            RegistryValueKind.DWord => new RegistryValue(RegistryKind.Dword, unchecked((uint)(int)raw).ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.QWord => new RegistryValue(RegistryKind.Qword, unchecked((ulong)(long)raw).ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.String => new RegistryValue(RegistryKind.String, (string)raw),
            _ => throw new NotSupportedException($"Registry value '{path}|{name}' has unsupported kind {kind}; only REG_DWORD, REG_QWORD and REG_SZ are supported."),
        };
    }

    public void Write(string path, string name, RegistryValue value)
    {
        var (hive, subKey) = Split(path);
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.CreateSubKey(subKey, writable: true);
        switch (value.Kind)
        {
            case RegistryKind.Dword:
                key.SetValue(name, unchecked((int)uint.Parse(value.Data, CultureInfo.InvariantCulture)), RegistryValueKind.DWord);
                break;
            case RegistryKind.Qword:
                key.SetValue(name, unchecked((long)ulong.Parse(value.Data, CultureInfo.InvariantCulture)), RegistryValueKind.QWord);
                break;
            default:
                key.SetValue(name, value.Data, RegistryValueKind.String);
                break;
        }
    }

    public void Delete(string path, string name)
    {
        var (hive, subKey) = Split(path);
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public IReadOnlyList<string> GetSubKeyNames(string path)
    {
        var (hive, subKey) = Split(path);
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(subKey, writable: false);
        return key?.GetSubKeyNames() ?? [];
    }

    public IReadOnlyList<string> GetValueNames(string path)
    {
        var (hive, subKey) = Split(path);
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(subKey, writable: false);
        return key?.GetValueNames().Where(n => n.Length > 0).ToArray() ?? [];
    }

    private static (RegistryHive Hive, string SubKey) Split(string path)
    {
        var slash = path.IndexOf('\\');
        var root = slash < 0 ? path : path[..slash];
        var subKey = slash < 0 ? string.Empty : path[(slash + 1)..];
        var hive = root.ToUpperInvariant() switch
        {
            "HKLM" => RegistryHive.LocalMachine,
            "HKCU" => RegistryHive.CurrentUser,
            _ => throw new ArgumentException($"Unknown registry hive in path '{path}'.", nameof(path)),
        };
        return (hive, subKey);
    }
}
