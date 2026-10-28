using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Actions;

/// <param name="createdSchemes">Schemes created by unreverted journal entries, mapped to the scheme each one duplicated (null when the entry predates that field). An active created scheme counts as applied for its source, so a second run never duplicates again.</param>
internal sealed class PowerSchemeHandler(Func<IReadOnlyDictionary<Guid, Guid?>> createdSchemes) : IActionHandler
{
    public const string Type = "powerScheme";
    private const string JournalTarget = "power";

    public async Task<bool> IsAppliedAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var target = TargetGuid((PowerSchemeAction)action);
        var active = await ctx.Services.Power.GetActiveAsync(ct).ConfigureAwait(false);
        return active == target
            || (createdSchemes().TryGetValue(active, out var source) && (source is null || source == target));
    }

    public async Task<JournalAction> CaptureAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var active = await ctx.Services.Power.GetActiveAsync(ct).ConfigureAwait(false);
        ctx.Log.Debug("Power scheme previous active: {Scheme}", active);
        return new JournalAction(Type, JournalTarget, Previous(active, null, null));
    }

    public async Task<JournalAction> ApplyAsync(CleanupAction action, JournalAction captured, StepContext ctx, CancellationToken ct)
    {
        var target = TargetGuid((PowerSchemeAction)action);
        var power = ctx.Services.Power;
        var schemes = await power.ListAsync(ct).ConfigureAwait(false);

        Guid? created = null;
        var duplicated = false;
        var activate = target;
        if (!schemes.Contains(target))
        {
            // Reuse a scheme an earlier run created from this target, so switching away and re-running never piles up duplicates.
            created = createdSchemes()
                .Where(c => c.Value == target && schemes.Contains(c.Key))
                .Select(c => (Guid?)c.Key)
                .FirstOrDefault();
            if (created is null)
            {
                created = await power.DuplicateAsync(target, ct).ConfigureAwait(false);
                duplicated = true;
                ctx.Log.Debug("Power scheme {Target} is hidden; created {Created}", target, created);
            }
            else
            {
                ctx.Log.Debug("Power scheme {Target} is hidden; reusing {Created}", target, created);
            }

            activate = created.Value;
        }

        try
        {
            await power.SetActiveAsync(activate, ct).ConfigureAwait(false);
            var active = await power.GetActiveAsync(ct).ConfigureAwait(false);
            if (active != activate)
            {
                throw new InvalidOperationException($"Active power scheme is {active}, expected {activate}.");
            }

            ctx.Log.Debug("Power scheme new active: {Scheme}", active);
        }
        catch when (duplicated)
        {
            // The journal never learns about the duplicate, so remove it here rather than leak it.
            try
            {
                await power.DeleteAsync(created!.Value, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupError)
            {
                ctx.Log.Warning(cleanupError, "Could not delete the power scheme {Created} created before the failure", created);
            }

            throw;
        }

        ctx.Log.Information("Power scheme {Scheme} activated", activate);
        if (created is null)
        {
            return captured;
        }

        var previous = Guid.Parse(captured.Previous.GetProperty("previous").GetString()!);
        return captured with { Previous = Previous(previous, created, target) };
    }

    /// <summary>Idempotent: activates the previous scheme (Balanced when it no longer exists) and deletes each recorded created scheme that still exists.</summary>
    public async Task UndoAsync(JournalAction recorded, StepContext ctx, CancellationToken ct)
    {
        var power = ctx.Services.Power;
        var existing = await power.ListAsync(ct).ConfigureAwait(false);
        var previous = Guid.Parse(recorded.Previous.GetProperty("previous").GetString()!);
        var activate = previous;
        if (!existing.Contains(previous))
        {
            ctx.Log.Warning("Power scheme {Previous} no longer exists; activating Balanced instead", previous);
            activate = BuiltInPowerSchemes.Balanced;
        }

        await power.SetActiveAsync(activate, ct).ConfigureAwait(false);

        foreach (var created in Created(recorded.Previous).Distinct())
        {
            if (created == activate || !existing.Contains(created))
            {
                ctx.Log.Debug("Power scheme {Created} is already gone or active; not deleting it", created);
                continue;
            }

            await power.DeleteAsync(created, ct).ConfigureAwait(false);
        }

        ctx.Log.Information("Power scheme restored to {Scheme}", activate);
    }

    /// <summary>Folds an item's unreverted power records, oldest first, into one: the earliest <c>previous</c> and every <c>created</c> scheme.</summary>
    internal static JournalAction MergeForUndo(IReadOnlyList<JournalAction> recorded)
    {
        var previous = recorded[0].Previous.GetProperty("previous").GetString();
        var created = recorded.SelectMany(r => Created(r.Previous)).Distinct().Select(g => g.ToString()).ToList();
        return recorded[0] with { Previous = JsonSerializer.SerializeToElement(new { previous, created }) };
    }

    /// <summary>The <c>created</c> field: one GUID string in an applied record, an array in a merged one.</summary>
    private static IEnumerable<Guid> Created(JsonElement previous)
    {
        if (!previous.TryGetProperty("created", out var created))
            return [];
        return created.ValueKind switch
        {
            JsonValueKind.String => [Guid.Parse(created.GetString()!)],
            JsonValueKind.Array => created.EnumerateArray().Select(e => Guid.Parse(e.GetString()!)).ToList(),
            _ => [],
        };
    }

    /// <summary>The schemes created by unreverted journal entries, with the scheme each duplicated: the source of the <c>createdSchemes</c> provider.</summary>
    internal static IReadOnlyDictionary<Guid, Guid?> CreatedSchemes(CleanupJournal? journal)
    {
        var created = new Dictionary<Guid, Guid?>();
        if (journal is null)
            return created;

        foreach (var action in journal.Entries.Where(e => e.RevertedAt is null).SelectMany(e => e.Actions))
        {
            if (action.Type == Type
                && action.Previous.ValueKind == JsonValueKind.Object
                && action.Previous.TryGetProperty("created", out var value)
                && value.ValueKind == JsonValueKind.String
                && Guid.TryParse(value.GetString(), out var scheme))
            {
                created[scheme] = action.Previous.TryGetProperty("source", out var source)
                    && source.ValueKind == JsonValueKind.String
                    && Guid.TryParse(source.GetString(), out var from)
                        ? from
                        : null;
            }
        }

        return created;
    }

    /// <param name="source">The scheme <paramref name="created"/> duplicated.</param>
    private static JsonElement Previous(Guid previous, Guid? created, Guid? source) =>
        JsonSerializer.SerializeToElement(new { previous = previous.ToString(), created = created?.ToString(), source = source?.ToString() });

    private static Guid TargetGuid(PowerSchemeAction action) => action.Scheme switch
    {
        "balanced" => BuiltInPowerSchemes.Balanced,
        "highPerformance" => BuiltInPowerSchemes.HighPerformance,
        "ultimate" => BuiltInPowerSchemes.Ultimate,
        _ => throw new ArgumentException($"Unknown power scheme '{action.Scheme}'.", nameof(action)),
    };
}
