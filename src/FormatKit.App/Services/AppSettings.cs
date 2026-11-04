using System.IO;
using System.Text.Json;
using FormatKit.Core.SystemAccess;

namespace FormatKit.App.Services;

/// <summary>
/// The window's own settings, <c>{"lastProfile": "...", "checkForUpdates": false}</c> in
/// <c>%LOCALAPPDATA%\FormatKit\app.json</c>. A missing or corrupt file reads as the defaults, and a failed write is
/// ignored: these settings are a convenience.
/// </summary>
public sealed class AppSettings
{
    private readonly IFileSystem _files;
    private readonly string _path;
    private string? _lastProfile;
    private bool _checkForUpdates = true;

    public AppSettings(IFileSystem files, string path)
    {
        _files = files;
        _path = path;
        Read();
    }

    public string? LastProfile
    {
        get => _lastProfile;
        set
        {
            _lastProfile = value;
            Write();
        }
    }

    /// <summary>Whether the window looks for a newer release when it opens.</summary>
    public bool CheckForUpdates
    {
        get => _checkForUpdates;
        set
        {
            _checkForUpdates = value;
            Write();
        }
    }

    private void Read()
    {
        try
        {
            if (!_files.FileExists(_path))
                return;

            using var doc = JsonDocument.Parse(_files.ReadAllText(_path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return;

            if (doc.RootElement.TryGetProperty("lastProfile", out var last) && last.ValueKind == JsonValueKind.String)
                _lastProfile = last.GetString();
            if (doc.RootElement.TryGetProperty("checkForUpdates", out var check) && check.ValueKind == JsonValueKind.False)
                _checkForUpdates = false;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Defaults stay.
        }
    }

    private void Write()
    {
        try
        {
            if (Path.GetDirectoryName(_path) is { Length: > 0 } dir)
                _files.CreateDirectory(dir);
            var values = new Dictionary<string, object?> { ["lastProfile"] = _lastProfile };
            if (!_checkForUpdates)
                values["checkForUpdates"] = false;
            _files.WriteAllTextAtomic(_path, JsonSerializer.Serialize(values));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
