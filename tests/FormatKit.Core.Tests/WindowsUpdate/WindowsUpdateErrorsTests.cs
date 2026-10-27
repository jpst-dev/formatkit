using FormatKit.Core.WindowsUpdate;

namespace FormatKit.Core.Tests.WindowsUpdate;

public class WindowsUpdateErrorsTests
{
    [Theory]
    [InlineData(unchecked((int)0x8024402C))]
    [InlineData(unchecked((int)0x80070422))]
    [InlineData(unchecked((int)0x8024001E))]
    public void Known_codes_have_specific_text(int hresult)
    {
        var text = WindowsUpdateErrors.Describe(hresult);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("0x", text);
    }

    [Fact]
    public void Unknown_code_includes_hex()
    {
        Assert.Contains("0x80240FFF", WindowsUpdateErrors.Describe(unchecked((int)0x80240FFF)));
    }
}
