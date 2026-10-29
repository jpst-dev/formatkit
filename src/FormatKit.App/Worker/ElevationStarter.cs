using System.ComponentModel;
using System.Diagnostics;

namespace FormatKit.App.Worker;

/// <summary>
/// Starts through the shell (<c>UseShellExecute</c>), with the <c>runas</c> verb when elevating. Arguments go through
/// <see cref="ProcessStartInfo.ArgumentList"/>, quoted by .NET, never as a hand-built command line.
/// </summary>
public sealed class ElevationStarter : IElevationStarter
{
    private const int ErrorCancelled = 1223;

    public Task<IStartedProcess?> StartAsync(string exe, IReadOnlyList<string> args, bool runas) =>
        // Off the UI thread: the start blocks while the UAC prompt is up.
        Task.Run<IStartedProcess?>(() =>
        {
            var startInfo = new ProcessStartInfo(exe) { UseShellExecute = true };
            if (runas) startInfo.Verb = "runas";
            foreach (var argument in args)
            {
                startInfo.ArgumentList.Add(argument);
            }

            try
            {
                var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException($"No process was started for {exe}.");
                return new StartedProcess(process);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return null; // UAC declined
            }
        });

    private sealed class StartedProcess(Process process) : IStartedProcess
    {
        public Task<int> Exited { get; } = WaitAsync(process);

        private static async Task<int> WaitAsync(Process process)
        {
            using (process)
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
                return process.ExitCode;
            }
        }
    }
}
