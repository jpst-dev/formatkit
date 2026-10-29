using CommunityToolkit.Mvvm.ComponentModel;
using FormatKit.App.Localization;
using FormatKit.Cleanup.Catalog;
using FormatKit.Programs.Catalog;

namespace FormatKit.App.ViewModels;

public sealed record CatalogDetails(string Title, IReadOnlyList<string> Lines);

/// <summary>One entry of a filter drop-down; a null <paramref name="Value"/> is "All".</summary>
public sealed record FilterOption<T>(T? Value, string Label) where T : struct;

/// <summary>Read-only browser of the cleanup and program catalogs with level, category and text filters.</summary>
public sealed partial class CatalogsViewModel : ObservableObject
{
    private readonly CleanupCatalog _cleanup;
    private readonly ProgramCatalog _programs;

    public CatalogsViewModel(CleanupCatalog cleanup, ProgramCatalog programs)
    {
        _cleanup = cleanup;
        _programs = programs;
        LevelOptions = Options<CleanupLevel>("Level_");
        CleanupCategoryOptions = Options<CleanupCategory>("CleanupCategory_");
        ProgramCategoryOptions = Options<ProgramCategory>("ProgramCategory_");
        _cleanupItems = cleanup.Items;
        _programItems = programs.Programs;
    }

    public IReadOnlyList<FilterOption<CleanupLevel>> LevelOptions { get; }

    public IReadOnlyList<FilterOption<CleanupCategory>> CleanupCategoryOptions { get; }

    public IReadOnlyList<FilterOption<ProgramCategory>> ProgramCategoryOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LevelOption))]
    private CleanupLevel? _levelFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CleanupCategoryOption))]
    private CleanupCategory? _cleanupCategoryFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgramCategoryOption))]
    private ProgramCategory? _programCategoryFilter;

    [ObservableProperty]
    private string _searchText = string.Empty;

    // A ComboBox cannot select a null value, so each filter has a drop-down view with an "All" entry.
    public FilterOption<CleanupLevel> LevelOption
    {
        get => LevelOptions.First(o => o.Value == LevelFilter);
        set => LevelFilter = value?.Value;
    }

    public FilterOption<CleanupCategory> CleanupCategoryOption
    {
        get => CleanupCategoryOptions.First(o => o.Value == CleanupCategoryFilter);
        set => CleanupCategoryFilter = value?.Value;
    }

    public FilterOption<ProgramCategory> ProgramCategoryOption
    {
        get => ProgramCategoryOptions.First(o => o.Value == ProgramCategoryFilter);
        set => ProgramCategoryFilter = value?.Value;
    }

    [ObservableProperty]
    private IReadOnlyList<CleanupItem> _cleanupItems;

    [ObservableProperty]
    private IReadOnlyList<CatalogProgram> _programItems;

    /// <summary>0 for the Cleanup tab, 1 for the Programs tab.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Selected), nameof(Details))]
    private int _selectedTab;

    // One selection per tab, so a list that reports null while its tab is hidden cannot clear the other.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Selected), nameof(Details))]
    private CleanupItem? _selectedCleanup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Selected), nameof(Details))]
    private CatalogProgram? _selectedProgram;

    /// <summary>The selected <see cref="CleanupItem"/> or <see cref="CatalogProgram"/> of the active tab.</summary>
    public object? Selected => SelectedTab == 0 ? SelectedCleanup : SelectedProgram;

    public CatalogDetails? Details => Selected switch
    {
        CleanupItem item => CleanupDetails(item),
        CatalogProgram program => ProgramDetails(program),
        _ => null,
    };

    partial void OnLevelFilterChanged(CleanupLevel? value) => Refresh();

    partial void OnCleanupCategoryFilterChanged(CleanupCategory? value) => Refresh();

    partial void OnProgramCategoryFilterChanged(ProgramCategory? value) => Refresh();

    partial void OnSearchTextChanged(string value) => Refresh();

    private void Refresh()
    {
        var term = SearchText.Trim();
        CleanupItems = _cleanup.Filter(LevelFilter, CleanupCategoryFilter)
            .Where(i => Matches(term, i.Id, i.LocalizedText().Title))
            .ToList();
        ProgramItems = _programs.Programs
            .Where(p => ProgramCategoryFilter is null || p.Category == ProgramCategoryFilter)
            .Where(p => Matches(term, p.Id, p.LocalizedText().Name))
            .ToList();
        if (SelectedCleanup is not null && !CleanupItems.Contains(SelectedCleanup))
            SelectedCleanup = null;
        if (SelectedProgram is not null && !ProgramItems.Contains(SelectedProgram))
            SelectedProgram = null;
    }

    private static bool Matches(string term, string id, string title) =>
        term.Length == 0
        || id.Contains(term, StringComparison.OrdinalIgnoreCase)
        || title.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static CatalogDetails CleanupDetails(CleanupItem item)
    {
        var text = item.LocalizedText();
        List<string> lines =
        [
            Strings.Format("Cat_Level", Strings.Get("Level_" + item.Level)),
            Strings.Format("Cat_What", text.What),
            Strings.Format("Cat_Breaks", text.Breaks),
            Strings.Format("Cat_Gain", text.Gain),
        ];
        if (item.Contested)
            lines.Add(Strings.Get("Cat_Contested"));
        if (item.LaptopSensitive)
            lines.Add(Strings.Get("Cat_Laptop"));
        return new CatalogDetails(text.Title, lines);
    }

    private static CatalogDetails ProgramDetails(CatalogProgram program)
    {
        var text = program.LocalizedText();
        return new CatalogDetails(text.Name,
        [
            text.Description,
            Strings.Format("Cat_Winget", program.WingetId),
            Strings.Format("Cat_Source", program.Source),
            Strings.Format("Cat_Context", Strings.Get("Cat_Context_" + program.Context)),
        ]);
    }

    private static List<FilterOption<T>> Options<T>(string prefix) where T : struct, Enum =>
        [new FilterOption<T>(null, Strings.Get("Cat_All")),
            .. Enum.GetValues<T>().Select(v => new FilterOption<T>(v, Strings.Get(prefix + v)))];
}
