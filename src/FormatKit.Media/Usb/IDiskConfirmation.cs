namespace FormatKit.Media.Usb;

/// <summary>
/// Gets what the user typed to confirm erasing a disk: the CLI reads the console, the UI's worker takes the window's
/// answer. The check of the answer stays in <see cref="UsbTarget.ConfirmAndPrepareAsync"/>.
/// </summary>
public interface IDiskConfirmation
{
    /// <summary>What the user typed for <paramref name="disk"/>; null when there is no answer.</summary>
    Task<string?> AskAsync(UsbCandidate disk, CancellationToken ct);
}
