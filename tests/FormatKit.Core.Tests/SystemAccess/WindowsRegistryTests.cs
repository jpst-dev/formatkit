using FormatKit.Core.SystemAccess;
using Microsoft.Win32;

namespace FormatKit.Core.Tests.SystemAccess;

[Trait("Category", "Integration")]
public sealed class WindowsRegistryTests : IDisposable
{
    private readonly string _root = $@"HKCU\Software\FormatKitTests\{Guid.NewGuid():N}";
    private readonly WindowsRegistry _registry = new();

    public void Dispose()
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        hive.DeleteSubKeyTree(_root["HKCU\\".Length..], throwOnMissingSubKey: false);
    }

    [Fact]
    public void User_shell_folders_personal_reads_as_expand_string()
    {
        var value = _registry.Read(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", "Personal");

        Assert.Equal(RegistryKind.ExpandString, value?.Kind);
    }

    [Fact]
    public void Dword_above_int_max_round_trips()
    {
        _registry.Write(_root, "v", new RegistryValue(RegistryKind.Dword, "4294967295"));

        Assert.Equal(new RegistryValue(RegistryKind.Dword, "4294967295"), _registry.Read(_root, "v"));
    }

    [Fact]
    public void Qword_round_trips()
    {
        _registry.Write(_root, "v", new RegistryValue(RegistryKind.Qword, "18446744073709551615"));

        Assert.Equal(new RegistryValue(RegistryKind.Qword, "18446744073709551615"), _registry.Read(_root, "v"));
    }

    [Fact]
    public void String_round_trips()
    {
        _registry.Write(_root, "v", new RegistryValue(RegistryKind.String, "1"));

        Assert.Equal(new RegistryValue(RegistryKind.String, "1"), _registry.Read(_root, "v"));
    }

    [Fact]
    public void Read_unsupported_kind_throws()
    {
        using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
        using (var key = hive.CreateSubKey(_root["HKCU\\".Length..], writable: true))
        {
            key.SetValue("multi", new[] { "a", "b" }, RegistryValueKind.MultiString);
        }

        var ex = Assert.Throws<NotSupportedException>(() => _registry.Read(_root, "multi"));
        Assert.Contains("multi", ex.Message);
        Assert.Contains("MultiString", ex.Message);
    }

    [Fact]
    public void Read_missing_value_returns_null()
    {
        _registry.Write(_root, "other", new RegistryValue(RegistryKind.String, "x"));

        Assert.Null(_registry.Read(_root, "missing"));
    }

    [Fact]
    public void Read_missing_key_returns_null() =>
        Assert.Null(_registry.Read(_root + @"\nokey", "v"));

    [Fact]
    public void Delete_missing_value_does_not_throw()
    {
        _registry.Delete(_root + @"\nokey", "v");
        _registry.Write(_root, "other", new RegistryValue(RegistryKind.String, "x"));
        _registry.Delete(_root, "missing");
    }

    [Fact]
    public void GetSubKeyNames_lists_children()
    {
        _registry.Write(_root + @"\a", "v", new RegistryValue(RegistryKind.String, "1"));
        _registry.Write(_root + @"\b", "v", new RegistryValue(RegistryKind.String, "1"));

        Assert.Equal(["a", "b"], _registry.GetSubKeyNames(_root).Order().ToArray());
    }

    [Fact]
    public void GetSubKeyNames_of_missing_key_is_empty() =>
        Assert.Empty(_registry.GetSubKeyNames(_root + @"\nokey"));

    [Fact]
    public void Unknown_hive_throws_ArgumentException() =>
        Assert.Throws<ArgumentException>(() => _registry.Read(@"HKCR\x", "v"));
}
