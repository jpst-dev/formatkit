using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class PowerShellWindowsImagesTests
{
    private static ProcessResult Result(int exit, string stdout = "", string stderr = "") =>
        new(exit, stdout, stderr, TimeSpan.Zero, false);

    private static (PowerShellWindowsImages Images, FakeProcessRunner Runner) Create(ProcessResult result)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("powershell.exe", result);
        return (new PowerShellWindowsImages(runner), runner);
    }

    [Fact]
    public async Task List_ParsesTwoImages()
    {
        var json = """[{"Index":1,"Name":"Windows 11 Home","Architecture":9,"Version":"10.0.26100.1742","Languages":["pt-BR"],"DefaultLanguageIndex":0},{"Index":6,"Name":"Windows 11 Pro","Architecture":9,"Version":"10.0.26100.1742","Languages":["pt-BR","en-US"],"DefaultLanguageIndex":1}]""";
        var (images, runner) = Create(Result(0, json));

        var list = await images.ListAsync(@"E:\sources\install.wim", TestContext.Current.CancellationToken);

        Assert.Equal(2, list.Count);
        Assert.Equal(1, list[0].Index);
        Assert.Equal("Windows 11 Home", list[0].Name);
        Assert.Equal(9, list[0].Architecture);
        Assert.Equal("10.0.26100.1742", list[0].Version);
        Assert.Equal(["pt-BR"], list[0].Languages);
        Assert.Equal(6, list[1].Index);
        Assert.Equal(["pt-BR", "en-US"], list[1].Languages);
        Assert.Equal(1, list[1].DefaultLanguageIndex);
        var script = runner.Calls[0].Arguments[^1];
        Assert.Contains(@"Get-WindowsImage -ImagePath 'E:\sources\install.wim'", script);
        Assert.Contains("-Index", script);
    }

    [Fact]
    public async Task List_SingleObject_IsOneImage()
    {
        var json = """{"Index":1,"Name":"Windows 11 Pro","Architecture":9,"Version":"10.0.26100.1","Languages":["en-US"],"DefaultLanguageIndex":0}""";
        var (images, _) = Create(Result(0, json));

        var list = await images.ListAsync("a.wim", TestContext.Current.CancellationToken);

        Assert.Single(list);
    }

    [Fact]
    public async Task List_LanguagesAsString_IsOneItemList()
    {
        var json = """{"Index":1,"Name":"Windows 11 Pro","Architecture":9,"Version":"10.0.26100.1","Languages":"en-US","DefaultLanguageIndex":0}""";
        var (images, _) = Create(Result(0, json));

        var list = await images.ListAsync("a.wim", TestContext.Current.CancellationToken);

        Assert.Equal(["en-US"], Assert.Single(list).Languages);
    }

    [Fact]
    public async Task List_NonZeroExit_Throws()
    {
        var (images, _) = Create(Result(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => images.ListAsync("a.wim", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task List_EmptyOutput_Throws()
    {
        var (images, _) = Create(Result(0));

        await Assert.ThrowsAsync<InvalidOperationException>(() => images.ListAsync("a.wim", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task List_BadJson_Throws()
    {
        var (images, _) = Create(Result(0, "not json"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => images.ListAsync("a.wim", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Split_RunsSplitWindowImage()
    {
        var (images, runner) = Create(Result(0));

        await images.SplitAsync(@"E:\sources\install.wim", @"F:\sources\install.swm", 3800, TestContext.Current.CancellationToken);

        var script = runner.Calls[0].Arguments[^1];
        Assert.Contains(@"Split-WindowsImage -ImagePath 'E:\sources\install.wim'", script);
        Assert.Contains(@"-SplitImagePath 'F:\sources\install.swm'", script);
        Assert.Contains("-FileSize 3800", script);
        Assert.DoesNotContain("-CheckIntegrity", script);
        Assert.Equal(Timeouts.Media, runner.Calls[0].Timeout);
    }

    [Fact]
    public async Task Split_NonZeroExit_Throws()
    {
        var (images, _) = Create(Result(1));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => images.SplitAsync("a.wim", "b.swm", 3800, TestContext.Current.CancellationToken));
        Assert.Contains("1", ex.Message);
    }
}
