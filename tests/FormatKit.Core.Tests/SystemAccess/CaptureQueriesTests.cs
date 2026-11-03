using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

/// <summary>The read-only system queries that "profile capture" needs.</summary>
public class CaptureQueriesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProcessResult Result(int exit = 0, string stdout = "") =>
        new(exit, stdout, "", TimeSpan.Zero, false);

    [Fact]
    public async Task Export_can_leave_source_agreements_alone()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("winget.exe", Result());

        await new WingetClient(runner).ExportAsync(@"C:\tmp\a.json", Ct, acceptSourceAgreements: false);

        Assert.Equal(["export", "-o", @"C:\tmp\a.json", "--disable-interactivity"], runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task ExecAsDefaultUser_passes_no_user()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("wsl.exe", Result(stdout: "joao\n"));

        await new WslClient(runner).ExecAsDefaultUserAsync("Ubuntu", ["whoami"], Ct);

        Assert.Equal(["-d", "Ubuntu", "--exec", "whoami"], runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task Get_parses_size()
    {
        var runner = new FakeProcessRunner();
        var files = new InMemoryFileSystem();
        files.Seed(@"D:\dev.vhdx", "vhdx");
        runner.Enqueue("powershell.exe", Result(stdout: """{"Attached":true,"Letter":"D","Label":"Dev","Size":107374182400}"""));

        var state = await new PowerShellVirtualDisks(runner, files, new AppPaths(@"C:\FormatKitTest")).GetAsync(@"D:\dev.vhdx", Ct);

        Assert.Equal(107374182400, state!.SizeBytes);
        Assert.Contains("Size = $img.Size", runner.Calls[0].Arguments[^1]);
    }

    [Fact]
    public async Task Get_without_size_gives_null()
    {
        var runner = new FakeProcessRunner();
        var files = new InMemoryFileSystem();
        files.Seed(@"D:\dev.vhdx", "vhdx");
        runner.Enqueue("powershell.exe", Result(stdout: """{"Attached":false,"Letter":null,"Label":null,"Size":null}"""));

        var state = await new PowerShellVirtualDisks(runner, files, new AppPaths(@"C:\FormatKitTest")).GetAsync(@"D:\dev.vhdx", Ct);

        Assert.Null(state!.SizeBytes);
    }
}
