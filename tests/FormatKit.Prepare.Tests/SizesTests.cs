using System.Globalization;

namespace FormatKit.Prepare.Tests;

public class SizesTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1288490189L, "1.2 GB")]
    [InlineData(891289600L, "850.0 MB")]
    public void FormatsBinaryUnits(long bytes, string expected)
    {
        var saved = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try { Assert.Equal(expected, Sizes.Format(bytes)); }
        finally { CultureInfo.CurrentUICulture = saved; }
    }
}
