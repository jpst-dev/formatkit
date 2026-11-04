using FormatKit.Cli;
using FormatKit.Core.SystemAccess;
using FormatKit.Hosting;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Clears what an earlier update could not delete while that version was running.
Updater.RemoveLeftovers(new FileSystem(), Environment.ProcessPath!);

var verbose = args.Contains("--verbose", StringComparer.Ordinal);
var env = CliEnvironment.FromHost(
    Services.CreateReal(verbose, new HostOptions(["run", "--resume", "--wait-on-exit"], ConsoleLog: true)),
    Console.Out);
try
{
    return await new CliApp(env).RunAsync(args, cts.Token);
}
finally
{
    (env.Log as IDisposable)?.Dispose();
}
