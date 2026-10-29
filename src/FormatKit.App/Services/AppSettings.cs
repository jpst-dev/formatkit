using System.IO;
using System.Text.Json;
using FormatKit.Core.SystemAccess;

namespace FormatKit.App.Services;

/// <summary>
/// The window's own settings, <c>{"lastProfile": "..."}</c> in <c>%LOCALAPPDATA%\FormatKit\app.json</c>. A missing or
/// corrupt file reads as empty, and a failed write is ignored: remembering the last profile is a convenience.
/// </summary>
public sealed class AppSettings
{
    private readonly IFileSystem _files;
    private readonly string _path;
    private string? _lastProfile;

    public AppSettings(IFileSystem files, string path)
    {
        _files = files;
        _path = path;
        _lastProfile = Read();
    }

    public string? LastProfile
    {
        get => _lastProfile;
        set
        {
            _lastProfile = value;
            Write(value);
        }
    }

    private string? Read()
    {
        try
        {
            if (!_files.FileExists(_path))
                return null;

            using var doc = JsonDocument.Parse(_files.ReadAllText(_path));
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("lastProfile", out var last)
                && last.ValueKind == JsonValueKind.String
                    ? last.GetString()
                    : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Write(string? lastProfile)
    {
        try
        {
            if (Path.GetDirectoryName(_path) is { Length: > 0 } dir)
                _files.CreateDirectory(dir);
            _files.WriteAllTextAtomic(_path, JsonSerializer.Serialize(new Dictionary<string, string?> { ["lastProfile"] = lastProfile }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
