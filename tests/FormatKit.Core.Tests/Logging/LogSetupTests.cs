using FormatKit.Core.Logging;
using Microsoft.Extensions.Time.Testing;

namespace FormatKit.Core.Tests.Logging;

public class LogSetupTests
{
    [Fact]
    public void Log_file_is_created_in_logs_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "formatkit-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 14, 3, 5, TimeSpan.Zero));

            var logger = LogSetup.Create(paths, clock, verbose: false);
            logger.Information("hello");
            ((IDisposable)logger).Dispose();

            var file = Path.Combine(paths.LogsDirectory, "run-20261027-140305.log");
            Assert.True(File.Exists(file));
            Assert.Contains("hello", File.ReadAllText(file));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
