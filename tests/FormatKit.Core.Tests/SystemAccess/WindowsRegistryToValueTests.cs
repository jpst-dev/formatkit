using FormatKit.Core.SystemAccess;
using Microsoft.Win32;

namespace FormatKit.Core.Tests.SystemAccess;

/// <summary>Converts raw values without touching the host registry.</summary>
public sealed class WindowsRegistryToValueTests
{
    [Fact]
    public void Expand_string_keeps_the_text_unexpanded()
    {
        var value = WindowsRegistry.ToValue(@"HKCU\Software\X", "v", RegistryValueKind.ExpandString, @"%USERPROFILE%\x");

        Assert.Equal(new RegistryValue(RegistryKind.ExpandString, @"%USERPROFILE%\x"), value);
    }

    [Fact]
    public void Multi_string_is_not_supported()
    {
        Assert.Throws<NotSupportedException>(() =>
            WindowsRegistry.ToValue(@"HKCU\Software\X", "v", RegistryValueKind.MultiString, new[] { "a" }));
    }

    [Fact]
    public void Binary_reads_as_lowercase_hex_without_separators()
    {
        var value = WindowsRegistry.ToValue(@"HKCU\Software\X", "v", RegistryValueKind.Binary, new byte[] { 0x03, 0, 0xAB, 0xFF });

        Assert.Equal(new RegistryValue(RegistryKind.Binary, "0300abff"), value);
    }

    [Fact]
    public void Empty_binary_reads_as_empty_hex()
    {
        var value = WindowsRegistry.ToValue(@"HKCU\Software\X", "v", RegistryValueKind.Binary, Array.Empty<byte>());

        Assert.Equal(new RegistryValue(RegistryKind.Binary, ""), value);
    }

    [Fact]
    public void Hex_converts_to_bytes_case_insensitively()
    {
        Assert.Equal(new byte[] { 0x03, 0, 0xAB, 0xFF }, WindowsRegistry.ToBytes("0300AbFF"));
    }

    [Theory]
    [InlineData(RegistryKind.String, RegistryValueKind.String)]
    [InlineData(RegistryKind.ExpandString, RegistryValueKind.ExpandString)]
    public void Write_kind_maps_string_kinds_including_expand_string(RegistryKind kind, RegistryValueKind expected) =>
        Assert.Equal(expected, WindowsRegistry.ToStringValueKind(kind));
}
