using FormatKit.Core.SystemAccess;
using Serilog;

namespace FormatKit.Core.Tests.SystemAccess;

/// <summary>
/// DirectInstaller keeps the installer open through <see cref="FileSystem.OpenRead"/> while it runs. This proves on the
/// real OS that an executable can still be started while that handle is held. It runs a copy of hostname.exe, which
/// only prints the computer name; no installer runs.
/// </summary>
public class HeldFileLaunchIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "formatkit-held-" + Guid.NewGuid().ToString("N"));

    public HeldFileLaunchIntegrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Exe_starts_while_held_open_for_read_with_read_sharing()
    {
        var exe = Path.Combine(_dir, "held.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "hostname.exe"), exe);

        ProcessResult result;
        using (new FileSystem().OpenRead(exe))
        {
            result = await new ProcessRunner(new LoggerConfiguration().CreateLogger())
                .RunAsync(new ProcessSpec(exe, [], TimeSpan.FromSeconds(30)), CancellationToken.None);
        }

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(Environment.MachineName, result.StdOut, StringComparison.OrdinalIgnoreCase);
    }
}
