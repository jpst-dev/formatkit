using FormatKit.App.Worker;

namespace FormatKit.App.Tests;

public class MediaJobRequestTests
{
    public static TheoryData<MediaJobRequest> Valid() => new()
    {
        new MediaJobRequest(@"C:\my iso\ç.iso", null, null, @"C:\out dir", false),
        new MediaJobRequest(@"C:\a.iso", null, null, null, false),
        new MediaJobRequest(@"C:\a.iso", null, null, null, true),
        new MediaJobRequest(@"C:\a.iso", @"C:\p q.json", "hash", null, true),
        new MediaJobRequest(@"C:\a.iso", @"C:\p.json", "hash", @"C:\out", false),
    };

    [Theory]
    [MemberData(nameof(Valid))]
    public void Arguments_ParseBackToTheSameRequest(MediaJobRequest request) =>
        Assert.Equal(request, MediaJobRequest.Parse(request.ToArguments()));

    [Theory]
    [InlineData("media")]
    [InlineData("run", "--iso", "a", "--usb")]
    [InlineData("media", "--usb")]
    [InlineData("media", "--iso", "a")]
    [InlineData("media", "--iso", "a", "--usb", "--output", "b")]
    [InlineData("media", "--iso", "a", "--output", "b", "--allow-hard-disk")]
    [InlineData("media", "--iso", "a", "--usb", "--profile", "p")]
    [InlineData("media", "--iso", "a", "--usb", "--profile-hash", "h")]
    [InlineData("media", "--iso", "a", "--iso", "b", "--usb")]
    [InlineData("media", "--iso", "a", "--usb", "--usb")]
    [InlineData("media", "--iso", "a", "--usb", "--disk", "3")]
    [InlineData("media", "--iso")]
    public void Invalid_ParsesToNull(params string[] job) => Assert.Null(MediaJobRequest.Parse(job));
}
