namespace FormatKit.App;

/// <summary>
/// Keeps a failed start from leaving a windowless process behind: it would hold the instance mutex, and every later
/// start would only signal it.
/// </summary>
public static class StartupGuard
{
    /// <summary>
    /// Runs the window-mode startup. When it throws, the failure is reported and the app shuts down with exit code 1.
    /// Returns true when the startup completed.
    /// </summary>
    public static bool Run(Action startup, Action<Exception> report, Action<int> shutdown)
    {
        try
        {
            startup();
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                report(ex);
            }
            catch (Exception)
            {
                // Reporting failed too; shutting down still matters more.
            }

            shutdown(1);
            return false;
        }
    }

    /// <summary>After an unhandled exception the app keeps running only when its main window is loaded.</summary>
    public static bool ShutsDownAfterUnhandled(bool windowLoaded) => !windowLoaded;
}
