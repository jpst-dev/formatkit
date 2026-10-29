using System.Globalization;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Checks.Developer;

namespace FormatKit.Prepare.Tests.Checks.Developer;

public class WslCheckTests : IDisposable
{
    private readonly CultureInfo _saved = CultureInfo.CurrentUICulture;

    public WslCheckTests() => CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _saved;

    private static ProcessResult Result(int exit = 0, string stdout = "") => new(exit, stdout, "", TimeSpan.Zero, false);

    private static Task<IReadOnlyList<PrepareItem>> RunAsync(FakeProcessRunner runner) =>
        new WslCheck().RunAsync(PrepareTestContext.Create(TestServices.Create(processes: runner)), CancellationToken.None);

    [Fact]
    public void Identity()
    {
        var check = new WslCheck();
        Assert.Equal("dev.wsl", check.Id);
        Assert.Equal(PrepareGroup.Developer, check.Group);
    }

    [Fact]
    public async Task Docker_distros_are_skipped_and_each_other_distro_is_attention()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("wsl.exe", Result());
        runner.Enqueue("wsl.exe", Result(stdout: "Ubuntu\r\ndocker-desktop\r\ndocker-desktop-data\r\n"));

        var item = Assert.Single(await RunAsync(runner));

        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Equal("dev.wsl", item.CheckId);
        Assert.Contains("Ubuntu", item.Title);
        Assert.Contains(@"wsl --export Ubuntu E:\Ubuntu.tar", item.Action);
    }

    [Fact]
    public async Task Two_distros_give_two_items()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("wsl.exe", Result());
        runner.Enqueue("wsl.exe", Result(stdout: "Ubuntu\r\nDebian\r\n"));

        var items = await RunAsync(runner);

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Action!.Contains(@"wsl --export Debian E:\Debian.tar"));
    }

    [Fact]
    public async Task Wsl_unavailable_gives_no_item()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("wsl.exe", Result(exit: 1));

        Assert.Empty(await RunAsync(runner));
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Missing_wsl_exe_gives_no_item()
    {
        var runner = new FakeProcessRunner();
        runner.ThrowNotFound("wsl.exe");

        Assert.Empty(await RunAsync(runner));
    }

    [Fact]
    public async Task Only_docker_distros_give_no_item()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("wsl.exe", Result());
        runner.Enqueue("wsl.exe", Result(stdout: "docker-desktop\r\ndocker-desktop-data\r\n"));

        Assert.Empty(await RunAsync(runner));
    }
}
