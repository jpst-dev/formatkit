using System.Text.Json;
using FormatKit.Core;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Programs;

/// <summary>Remembers which direct installers already ran, so a resumed run skips them (ruling P2).</summary>
internal sealed class DirectProgress(IFileSystem files, AppPaths paths, Serilog.ILogger? log = null)
{
    private HashSet<string>? _keys;

    public bool IsDone(string key) => Load().Contains(key);

    public void MarkDone(string key)
    {
        var keys = Load();
        if (!keys.Add(key))
            return;

        // The installer already ran; failing to remember it only costs a repeat install on resume.
        try
        {
            files.CreateDirectory(paths.Root);
            files.WriteAllTextAtomic(paths.ProgramsDirectFile, JsonSerializer.Serialize(keys.Order(StringComparer.Ordinal)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Warning(ex, "Could not record progress in {File}", paths.ProgramsDirectFile);
        }
    }

    private HashSet<string> Load()
    {
        if (_keys is not null)
            return _keys;

        _keys = new HashSet<string>(StringComparer.Ordinal);
        if (!files.FileExists(paths.ProgramsDirectFile))
            return _keys;

        try
        {
            var stored = JsonSerializer.Deserialize<string[]>(files.ReadAllText(paths.ProgramsDirectFile));
            foreach (var key in stored ?? [])
                _keys.Add(key);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            log?.Warning(ex, "Ignoring unreadable {File}", paths.ProgramsDirectFile);
            _keys.Clear();
        }

        return _keys;
    }
}
