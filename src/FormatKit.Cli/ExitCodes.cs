namespace FormatKit.Cli;

public static class ExitCodes
{
    public const int Ok = 0;
    public const int CompletedWithFailures = 1;
    public const int InvalidInput = 2;
    public const int StoppedOnCriticalFailure = 3;
    public const int RebootPending = 4;
    public const int ElevationDeclined = 5;
    public const int NothingToResume = 6;

    /// <summary>The user pressed Ctrl+C (conventional 128 + SIGINT).</summary>
    public const int Cancelled = 130;
}
