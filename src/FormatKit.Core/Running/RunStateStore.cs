using System.Text.Json;
using System.Text.Json.Serialization;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Running;

public sealed class RunStateStore(IFileSystem files, AppPaths paths)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public StateLoadResult Load()
    {
        if (!files.FileExists(paths.StateFile))
            return new StateLoadResult(StateLoadStatus.None, null, null);

        try
        {
            var state = JsonSerializer.Deserialize<RunState>(files.ReadAllText(paths.StateFile), Options);
            if (state is null || state.ProfilePath is null || state.ProfileHash is null
                || state.PlanStepIds is null || state.Results is null)
                return Corrupt("State file is missing required fields.");
            return new StateLoadResult(StateLoadStatus.Loaded, state, null);
        }
        catch (JsonException ex)
        {
            return Corrupt(ex.Message);
        }
    }

    public void Save(RunState state)
    {
        files.CreateDirectory(Path.GetDirectoryName(paths.StateFile)!);
        files.WriteAllTextAtomic(paths.StateFile, JsonSerializer.Serialize(state, Options));
    }

    public void Delete()
    {
        if (files.FileExists(paths.StateFile))
            files.Delete(paths.StateFile);
    }

    private static StateLoadResult Corrupt(string error) => new(StateLoadStatus.Corrupt, null, error);
}
