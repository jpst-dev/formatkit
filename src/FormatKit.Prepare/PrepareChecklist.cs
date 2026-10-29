using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare;

public sealed record PrepareReport(IReadOnlyList<PrepareItem> Items)
{
    public bool HasBlockers => Items.Any(i => i.Status == PrepareStatus.Blocker);
}

/// <summary>Runs the checks in order; a check that throws becomes one NotChecked item and the others still run.</summary>
public sealed class PrepareChecklist(IReadOnlyList<IPrepareCheck> checks)
{
    public async Task<PrepareReport> RunAsync(PrepareContext context, CancellationToken ct)
    {
        var items = new List<PrepareItem>();
        foreach (var check in checks)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                items.AddRange(await check.RunAsync(context, ct));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                context.Log.Warning(ex, "Prepare check {CheckId} failed", check.Id);
                items.Add(new PrepareItem(
                    check.Id, check.Group, PrepareStatus.NotChecked,
                    Strings.Get("Item_NotChecked_Title"), Strings.Get("Item_NotChecked_Detail")));
            }
        }
        // OrderBy is stable, so a check's own item order is kept.
        var sorted = items
            .OrderBy(i => i.Status)
            .ThenBy(i => i.Group)
            .ThenBy(i => i.CheckId, StringComparer.Ordinal)
            .ToList();
        return new PrepareReport(sorted);
    }
}
