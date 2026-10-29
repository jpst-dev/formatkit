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
    public void Binary_is_not_supported()
    {
        Assert.Throws<NotSupportedException>(() =>
            WindowsRegistry.ToValue(@"HKCU\Software\X", "v", RegistryValueKind.Binary, new byte[] { 1 }));
    }

    [Fact]
    public void Write_rejects_expand_string()
    {
        Assert.Throws<NotSupportedException>(() =>
            new WindowsRegistry().Write(@"HKCU\Software\FormatKitTests\never-written", "v", new RegistryValue(RegistryKind.ExpandString, "x")));
    }
}
