using FormatKit.Programs.Catalog;

namespace FormatKit.Programs;

/// <summary>One program to install. <paramref name="Category"/> is null for programs outside the catalog (step <c>programs.other</c>).</summary>
public sealed record SelectedProgram(string Key, string DisplayName, ProgramCategory? Category, WingetEntry Package);

public static class ProgramSelector
{
    /// <summary>Catalog ids first, then <c>winget[]</c>, then imported; duplicates by (Id, Source) keep the first.</summary>
    public static IReadOnlyList<SelectedProgram> Select(ProgramsOptions options, ProgramCatalog catalog, IReadOnlyList<WingetEntry> imported)
    {
        var result = new List<SelectedProgram>();
        var seen = new HashSet<(string, string)>();

        bool Add(WingetEntry entry) =>
            seen.Add((entry.Id.ToUpperInvariant(), entry.Source.ToUpperInvariant()));

        foreach (var id in options.Catalog)
        {
            var program = catalog.Find(id);
            if (program is null)
                continue;
            var entry = new WingetEntry(program.WingetId, program.Source, null, program.Context, program.Scope);
            if (Add(entry))
                result.Add(new SelectedProgram(program.Id, program.LocalizedText().Name, program.Category, entry));
        }

        foreach (var entry in options.Winget.Concat(imported))
        {
            if (Add(entry))
                result.Add(new SelectedProgram($"winget:{entry.Source}:{entry.Id}", entry.Id, null, entry));
        }

        return result;
    }
}
