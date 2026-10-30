namespace FormatKit.App.Services;

/// <summary>User dialogs. No UI types in the signatures, so view models stay testable.</summary>
public interface IDialogService
{
    /// <summary>Asks for a file to open; <paramref name="filter"/> is a Windows file dialog filter. Null when cancelled.</summary>
    string? OpenFile(string filter);

    /// <summary>Asks where to save a file. Null when cancelled.</summary>
    string? SaveFile(string filter, string defaultName);

    bool Confirm(string title, string message);

    void ShowError(string message);

    void OpenFolder(string path);

    /// <summary>Counts down to a restart. True = restart now or the countdown elapsed, false = cancelled.</summary>
    Task<bool> RebootCountdownAsync(int seconds, CancellationToken ct);
}
