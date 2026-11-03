using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class PowerShellDiskImagesTests
{
    private static ProcessResult Result(int exit, string stdout = "", string stderr = "") =>
        new(exit, stdout, stderr, TimeSpan.Zero, false);

    private static (PowerShellDiskImages Images, FakeProcessRunner Runner) Create(ProcessResult result)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("powershell.exe", result);
        return (new PowerShellDiskImages(runner), runner);
    }

    private static string Script(FakeProcessRunner runner) => runner.Calls[0].Arguments[^1];

    [Fact]
    public async Task Mount_ReturnsRootFromDriveLetter()
    {
        var (images, runner) = Create(Result(0, "E\r\n"));

        var root = await images.MountAsync(@"C:\isos\win11.iso", TestContext.Current.CancellationToken);

        Assert.Equal(@"E:\", root);
        Assert.Contains(@"Mount-DiskImage -ImagePath 'C:\isos\win11.iso'", Script(runner));
        Assert.Equal(Timeouts.Query, runner.Calls[0].Timeout);
    }

    [Fact]
    public async Task Mount_QuotesPathWithApostrophe()
    {
        var (images, runner) = Create(Result(0, "E"));

        await images.MountAsync(@"C:\it's\win.iso", TestContext.Current.CancellationToken);

        Assert.Contains(@"'C:\it''s\win.iso'", Script(runner));
    }

    [Fact]
    public async Task Mount_NonZeroExit_Throws()
    {
        var (images, _) = Create(Result(1, "", "boom"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => images.MountAsync("a.iso", TestContext.Current.CancellationToken));
        Assert.Contains("1", ex.Message);
    }

    [Fact]
    public async Task Mount_EmptyOutput_Throws()
    {
        var (images, _) = Create(Result(0, "  \r\n"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => images.MountAsync("a.iso", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Dismount_RunsDismountDiskImage()
    {
        var (images, runner) = Create(Result(0));

        await images.DismountAsync(@"C:\isos\win11.iso", TestContext.Current.CancellationToken);

        Assert.Contains(@"Dismount-DiskImage -ImagePath 'C:\isos\win11.iso'", Script(runner));
    }

    [Fact]
    public async Task Dismount_NonZeroExit_Throws()
    {
        var (images, _) = Create(Result(1));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => images.DismountAsync("a.iso", TestContext.Current.CancellationToken));
        Assert.Contains("1", ex.Message);
    }
}
