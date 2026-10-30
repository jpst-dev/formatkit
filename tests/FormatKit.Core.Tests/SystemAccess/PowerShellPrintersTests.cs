using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class PowerShellPrintersTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_one_name_per_line()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("powershell.exe", new ProcessResult(0, "HP LaserJet\r\nFax\r\n\r\n", "", TimeSpan.Zero, false));

        var names = await new PowerShellPrinters(runner).ListNamesAsync(Ct);

        Assert.Equal(["HP LaserJet", "Fax"], names);
        Assert.Contains("Get-Printer", runner.Calls[0].Arguments[^1]);
    }

    [Fact]
    public async Task Throws_when_the_command_fails()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("powershell.exe", new ProcessResult(1, "", "no spooler", TimeSpan.Zero, false));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new PowerShellPrinters(runner).ListNamesAsync(Ct));
    }
}
