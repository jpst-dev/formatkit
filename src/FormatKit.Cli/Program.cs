using FormatKit.Cli;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var verbose = args.Contains("--verbose", StringComparer.Ordinal);
var env = Services.CreateReal(verbose);
try
{
    return await new CliApp(env).RunAsync(args, cts.Token);
}
finally
{
    (env.Log as IDisposable)?.Dispose();
}
