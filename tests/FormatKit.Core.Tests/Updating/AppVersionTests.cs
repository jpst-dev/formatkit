using FormatKit.Core.Updating;

namespace FormatKit.Core.Tests.Updating;

public sealed class AppVersionTests
{
    private static AppVersion Parse(string text)
    {
        Assert.True(AppVersion.TryParse(text, out var version));
        return version!;
    }

    [Theory]
    [InlineData("0.2.0", "0.2.1")]
    [InlineData("0.2.9", "0.3.0")]
    [InlineData("0.9.0", "1.0.0")]
    [InlineData("0.2.9", "0.10.0")]
    [InlineData("0.2.0-rc.1", "0.2.0")]
    [InlineData("0.2.0-rc.2", "0.2.0-rc.10")]
    [InlineData("0.2.0-alpha", "0.2.0-rc.1")]
    [InlineData("0.2.0-rc", "0.2.0-rc.1")]
    [InlineData("0.2.0-1", "0.2.0-rc")]
    public void Older_sorts_before_newer(string older, string newer)
    {
        Assert.True(Parse(older).CompareTo(Parse(newer)) < 0);
        Assert.True(Parse(newer).CompareTo(Parse(older)) > 0);
    }

    [Theory]
    [InlineData("v0.2.1", "0.2.1")]
    [InlineData("0.2.1+68fa7b7b81fff5f8", "0.2.1")]
    [InlineData("0.2.1-rc.1+abc", "0.2.1-rc.1")]
    public void Prefix_and_commit_are_ignored(string text, string same)
    {
        Assert.Equal(0, Parse(text).CompareTo(Parse(same)));
        Assert.Equal(same, Parse(text).Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("latest")]
    [InlineData("1.0.x")]
    [InlineData("99999999999.0.0")]
    public void Bad_text_does_not_parse(string? text) =>
        Assert.False(AppVersion.TryParse(text, out _));
}
