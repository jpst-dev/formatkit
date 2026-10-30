using FormatKit.Cli;
using FormatKit.Hosting;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

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
