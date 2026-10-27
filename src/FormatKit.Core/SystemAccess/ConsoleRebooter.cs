namespace FormatKit.Core.SystemAccess;

public sealed class ConsoleRebooter(IProcessRunner runner) : IRebooter
{
    public async Task<bool> RebootAsync(TimeSpan countdown, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + countdown;
        var nextPrint = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (!Console.IsInputRedirected && Console.KeyAvailable
                && Console.ReadKey(intercept: true).Key == ConsoleKey.Escape)
            {
                Console.WriteLine("Reboot cancelled.");
                return false;
            }

            if (DateTime.UtcNow >= nextPrint)
            {
                var remaining = (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds);
                Console.WriteLine($"Rebooting in {remaining} s (press Esc to cancel)...");
                nextPrint = DateTime.UtcNow.AddSeconds(10);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
        }

        await runner.RunAsync(new ProcessSpec("shutdown.exe", ["/r", "/t", "0"], Timeouts.Query), ct).ConfigureAwait(false);
        return true;
    }
}
