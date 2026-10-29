using System.Globalization;
using System.Runtime.InteropServices;

namespace FormatKit.Hosting.Tests;

[Collection("Culture")]
public sealed class SystemQueryTests
{
    [Fact]
    public async Task Guard_ComException_ReturnsLocalizedError()
    {
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        try
        {
            var result = await SystemQuery.GuardAsync<int>(() => throw new COMException("boom"));

            Assert.Equal(0, result.Value);
            Assert.Equal("Could not read the system information: boom", result.Error);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public async Task Guard_Success_ReturnsValue()
    {
        var result = await SystemQuery.GuardAsync(() => Task.FromResult(42));

        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }
}
