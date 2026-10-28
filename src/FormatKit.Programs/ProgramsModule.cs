using System.Text.Json;
using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Programs.Catalog;
using FormatKit.Programs.Localization;
using FormatKit.Programs.Steps;

namespace FormatKit.Programs;

/// <param name="files">The file system for the import file and install progress; the real one unless a test supplies another.</param>
/// <param name="log">Hears the warnings from reading the import file.</param>
public sealed class ProgramsModule(ProgramCatalog catalog, AppPaths paths, IFileSystem? files = null, Serilog.ILogger? log = null) : IModule
{
    public string Name => "programs";

    public bool IsEnabled(JsonElement section) =>
        section.ValueKind == JsonValueKind.Object
        && section.TryGetProperty("enabled", out var enabled)
        && enabled.ValueKind == JsonValueKind.True;

    public IReadOnlyList<ProfileError> Validate(JsonElement section, string path) => Validate(section, path, null);

    /// <param name="baseDirectory">The profile file's directory; relative importFile and direct paths resolve against it.</param>
    public IReadOnlyList<ProfileError> Validate(JsonElement section, string path, string? baseDirectory) =>
        ProgramsOptions.Parse(section, path, catalog, files ?? new FileSystem(), baseDirectory).Errors;

    public IReadOnlyList<IStep> CreateSteps(JsonElement section) => CreateSteps(section, null);

    /// <param name="baseDirectory">The profile file's directory; relative importFile and direct paths resolve against it.</param>
    public IReadOnlyList<IStep> CreateSteps(JsonElement section, string? baseDirectory)
    {
        if (!IsEnabled(section))
            return [];

        var (options, _) = ProgramsOptions.Parse(section, "programs", catalog, files ?? new FileSystem(), baseDirectory);
        if (options is null)
            return [];

        foreach (var warning in options.Warnings)
            log?.Warning("{Warning}", warning);

        var selected = ProgramSelector.Select(options, catalog, options.ImportedPrograms);
        var steps = new List<IStep>();

        foreach (var category in Enum.GetValues<ProgramCategory>())
        {
            var items = selected.Where(p => p.Category == category).ToList();
            if (items.Count == 0)
                continue;
            var name = category.ToString();
            steps.Add(new WingetProgramsStep(
                $"programs.{char.ToLowerInvariant(name[0])}{name[1..]}", Strings.Get($"Step_Title_{name}"), items, options.Upgrade));
        }

        var other = selected.Where(p => p.Category is null).ToList();
        if (other.Count > 0)
            steps.Add(new WingetProgramsStep("programs.other", Strings.Get("Step_Title_Other"), other, options.Upgrade));

        if (options.Direct.Count > 0)
            steps.Add(new DirectProgramsStep(options.Direct, paths));

        return steps;
    }
}
