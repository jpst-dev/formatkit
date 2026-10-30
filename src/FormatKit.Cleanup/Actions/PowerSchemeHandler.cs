using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Cleanup.Localization;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Actions;

/// <param name="createdSchemes">Schemes created by unreverted journal entries, mapped to the scheme each one duplicated (null when the entry predates that field). An active created scheme counts as applied for its source, so a second run never duplicates again.</param>
/// <param name="formatKitSchemes">The created schemes that are FormatKit performance plans (journaled with <c>formatkit: true</c>); older journals have none.</param>
internal sealed class PowerSchemeHandler(
    Func<IReadOnlyDictionary<Guid, Guid?>> createdSchemes,
    Func<IReadOnlySet<Guid>>? formatKitSchemes = null) : IActionHandler
{
    public const string Type = "powerScheme";
    private const string JournalTarget = "power";
    private const string FormatKitScheme = "formatkit";

    /// <summary>Written on both AC and DC, by GUID because powercfg aliases are missing on some builds. Spec 5.4.</summary>
    public static readonly IReadOnlyList<(string Subgroup, string Setting, int Value)> FormatKitSettings =
    [
        // Processor: core parking off (CPMINCORES), energy performance preference 0 (PERFEPP), aggressive boost (PERFBOOSTMODE).
        ("54533251-82be-4824-96c1-47b60b740d00", "0cc5b647-c1df-4637-891a-dec35c318583", 100),
        ("54533251-82be-4824-96c1-47b60b740d00", "36687f9e-e3a5-4dbf-b1dc-15eb381c6863", 0),
        ("54533251-82be-4824-96c1-47b60b740d00", "be337238-0d82-4146-a960-4f3749d470c7", 2),
        // PCI Express link state power management off (ASPM).
        ("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0),
        // USB selective suspend off.
        ("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0),
        // Hard disk never turns off (DISKIDLE).
        ("0012ee47-9041-4b5d-9b77-535fba8b1442", "6738e2c4-e8a5-4a42-b16a-e040e769756e", 0),
    ];

    public async Task<bool> IsAppliedAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var target = TargetGuid((PowerSchemeAction)action);
        var active = await ctx.Services.Power.GetActiveAsync(ct).ConfigureAwait(false);
        if (IsFormatKit(action))
            return FormatKitSet().Contains(active) && await FormatKitValuesMatchAsync(active, ctx, ct).ConfigureAwait(false);

        // The FormatKit scheme duplicates High performance but is not the plain plan, so it never satisfies another item.
        if (FormatKitSet().Contains(active))
            return false;

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
        if (IsFormatKit(action))
            return await ApplyFormatKitAsync(captured, schemes, ctx, ct).ConfigureAwait(false);

        var formatKit = FormatKitSet();
        Guid? created = null;
        var duplicated = false;
        var activate = target;
        if (!schemes.Contains(target))
        {
            // Reuse a scheme an earlier run created from this target, so switching away and re-running never piles up duplicates.
            created = createdSchemes()
                .Where(c => c.Value == target && schemes.Contains(c.Key) && !formatKit.Contains(c.Key))
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

    /// <summary>
    /// Reuses the scheme an earlier run created, or duplicates High performance and renames the copy; then writes the
    /// <see cref="FormatKitSettings"/> on it, skipping any the machine does not have, and activates it.
    /// </summary>
    private async Task<JournalAction> ApplyFormatKitAsync(JournalAction captured, IReadOnlyList<Guid> schemes, StepContext ctx, CancellationToken ct)
    {
        var power = ctx.Services.Power;
        var source = BuiltInPowerSchemes.HighPerformance;
        var scheme = FormatKitSet().FirstOrDefault(schemes.Contains);
        var duplicated = scheme == Guid.Empty;
        if (duplicated)
            scheme = await power.DuplicateAsync(source, ct).ConfigureAwait(false);
        else
            ctx.Log.Debug("Reusing the FormatKit power scheme {Created}", scheme);

        var written = new List<string>();
        try
        {
            if (duplicated)
                await power.RenameAsync(scheme, Strings.Get("PowerScheme_FormatKit_Name"), Strings.Get("PowerScheme_FormatKit_Description"), ct).ConfigureAwait(false);

            foreach (var (subgroup, setting, value) in FormatKitSettings)
            {
                if (await power.GetValueAsync(scheme, subgroup, setting, ct).ConfigureAwait(false) is null)
                {
                    ctx.Log.Information("Power setting {Subgroup}/{Setting} does not exist on this machine; skipped", subgroup, setting);
                    continue;
                }

                await power.SetValueAsync(scheme, subgroup, setting, value, value, ct).ConfigureAwait(false);
                if (await power.GetValueAsync(scheme, subgroup, setting, ct).ConfigureAwait(false) != (value, value))
                    throw new InvalidOperationException($"Power setting {subgroup}/{setting} did not take effect on scheme {scheme}.");
                written.Add($"{subgroup}/{setting}");
            }

            await power.SetActiveAsync(scheme, ct).ConfigureAwait(false);
            var active = await power.GetActiveAsync(ct).ConfigureAwait(false);
            if (active != scheme)
                throw new InvalidOperationException($"Active power scheme is {active}, expected {scheme}.");
        }
        catch when (duplicated)
        {
            try
            {
                await power.DeleteAsync(scheme, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupError)
            {
                ctx.Log.Warning(cleanupError, "Could not delete the power scheme {Created} created before the failure", scheme);
            }

            throw;
        }

        ctx.Log.Information("FormatKit power scheme {Scheme} activated with {Count} settings", scheme, written.Count);
        var previous = Guid.Parse(captured.Previous.GetProperty("previous").GetString()!);
        return captured with
        {
            Previous = JsonSerializer.SerializeToElement(new
            {
                previous = previous.ToString(),
                created = scheme.ToString(),
                source = source.ToString(),
                formatkit = true,
                settings = written,
            }),
        };
    }

    /// <summary>Every setting the machine has matches on AC and DC; one it does not have is ignored.</summary>
    private static async Task<bool> FormatKitValuesMatchAsync(Guid scheme, StepContext ctx, CancellationToken ct)
    {
        foreach (var (subgroup, setting, value) in FormatKitSettings)
        {
            var current = await ctx.Services.Power.GetValueAsync(scheme, subgroup, setting, ct).ConfigureAwait(false);
            if (current is { } c && c != (value, value))
                return false;
        }

        return true;
    }

    private IReadOnlySet<Guid> FormatKitSet() => formatKitSchemes?.Invoke() ?? new HashSet<Guid>();

    private static bool IsFormatKit(CleanupAction action) => ((PowerSchemeAction)action).Scheme == FormatKitScheme;

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

    /// <summary>The created schemes of unreverted entries marked <c>formatkit</c>: the FormatKit performance plans. Entries from before the marker existed are never in it.</summary>
    internal static IReadOnlySet<Guid> FormatKitSchemes(CleanupJournal? journal)
    {
        var schemes = new HashSet<Guid>();
        if (journal is null)
            return schemes;

        foreach (var action in journal.Entries.Where(e => e.RevertedAt is null).SelectMany(e => e.Actions))
        {
            if (action.Type == Type
                && action.Previous.ValueKind == JsonValueKind.Object
                && action.Previous.TryGetProperty("formatkit", out var flag)
                && flag.ValueKind == JsonValueKind.True
                && action.Previous.TryGetProperty("created", out var value)
                && value.ValueKind == JsonValueKind.String
                && Guid.TryParse(value.GetString(), out var scheme))
            {
                schemes.Add(scheme);
            }
        }

        return schemes;
    }

    /// <param name="source">The scheme <paramref name="created"/> duplicated.</param>
    private static JsonElement Previous(Guid previous, Guid? created, Guid? source) =>
        JsonSerializer.SerializeToElement(new { previous = previous.ToString(), created = created?.ToString(), source = source?.ToString() });

    private static Guid TargetGuid(PowerSchemeAction action) => action.Scheme switch
    {
        "balanced" => BuiltInPowerSchemes.Balanced,
        "highPerformance" => BuiltInPowerSchemes.HighPerformance,
        "ultimate" => BuiltInPowerSchemes.Ultimate,
        FormatKitScheme => BuiltInPowerSchemes.HighPerformance,
        _ => throw new ArgumentException($"Unknown power scheme '{action.Scheme}'.", nameof(action)),
    };
}
