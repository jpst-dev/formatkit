using FormatKit.App.Services;

namespace FormatKit.App.Tests.Fakes;

/// <summary>Records dialog calls and returns scripted answers.</summary>
public sealed class FakeDialogService : IDialogService
{
    public string? OpenFileResult { get; set; }
    public string? SaveFileResult { get; set; }
    public bool ConfirmResult { get; set; } = true;
    public bool RebootResult { get; set; } = true;

    public List<string> Calls { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> OpenedFolders { get; } = [];
    public List<(string Title, string Message)> Confirmations { get; } = [];
    public List<int> RebootCountdowns { get; } = [];

    public string? OpenFile(string filter)
    {
        Calls.Add($"OpenFile:{filter}");
        return OpenFileResult;
    }

    public string? SaveFile(string filter, string defaultName)
    {
        Calls.Add($"SaveFile:{filter}:{defaultName}");
        return SaveFileResult;
    }

    public bool Confirm(string title, string message)
    {
        Calls.Add("Confirm");
        Confirmations.Add((title, message));
        return ConfirmResult;
    }

    public void ShowError(string message)
    {
        Calls.Add("ShowError");
        Errors.Add(message);
    }

    public void OpenFolder(string path)
    {
        Calls.Add("OpenFolder");
        OpenedFolders.Add(path);
    }

    public Task<bool> RebootCountdownAsync(int seconds, CancellationToken ct)
    {
        Calls.Add("RebootCountdown");
        RebootCountdowns.Add(seconds);
        return Task.FromResult(RebootResult);
    }
}
