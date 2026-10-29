using FormatKit.Core;

namespace FormatKit.Cli.Tests;

/// <summary>Runs the real checklist against the machine it runs on. Read-only; lives here because only the CLI builds real services.</summary>
public sealed class RealMachineTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task Prepare_check_runs_on_this_machine_and_prints_a_summary()
    {
        var output = new StringWriter();
        var env = Services.CreateReal(verbose: false) with { Out = output };

        var exit = await new CliApp(env).RunAsync(["prepare", "check"], CancellationToken.None);

        Assert.True(exit is ExitCodes.Ok or ExitCodes.CompletedWithFailures, $"exit {exit}");
        var last = output.ToString().TrimEnd().Split(Environment.NewLine)[^1];
        Assert.False(string.IsNullOrWhiteSpace(last));
        Console.WriteLine(output.ToString());
    }
}
