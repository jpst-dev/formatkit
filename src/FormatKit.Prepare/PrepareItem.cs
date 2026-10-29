namespace FormatKit.Prepare;

/// <summary>Declaration order is the sort order of the report.</summary>
public enum PrepareStatus { Blocker, Attention, NotChecked, Ok }

/// <summary>Declaration order is the sort order of the report.</summary>
public enum PrepareGroup { Security, Data, Developer, Apps }

public sealed record PrepareItem(
    string CheckId,
    PrepareGroup Group,
    PrepareStatus Status,
    string Title,
    string? Detail = null,
    string? Action = null);
