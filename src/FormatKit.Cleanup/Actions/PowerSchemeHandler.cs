using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Actions;

/// <param name="createdSchemes">GUIDs of schemes created by unreverted journal entries; an active scheme in this set counts as applied, so a second run never duplicates again.</param>
internal sealed class PowerSchemeHandler(Func<IReadOnlySet<Guid>> createdSchemes) : IActionHandler
{
    public const string Type = "powerScheme";
    private const string JournalTarget = "power";

    public async Task<bool> IsAppliedAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var target = TargetGuid((PowerSchemeAction)action);
        var active = await ctx.Services.Power.GetActiveAsync(ct).ConfigureAwait(false);
        return active == target || createdSchemes().Contains(active);
    }

    public async Task<JournalAction> CaptureAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var active = await ctx.Services.Power.GetActiveAsync(ct).ConfigureAwait(false);
        ctx.Log.Debug("Power scheme previous active: {Scheme}", active);
        return new JournalAction(Type, JournalTarget, Previous(active, null));
    }

    public async Task<JournalAction> ApplyAsync(CleanupAction action, JournalAction captured, StepContext ctx, CancellationToken ct)
    {
        var target = TargetGuid((PowerSchemeAction)action);
        var power = ctx.Services.Power;
        var schemes = await power.ListAsync(ct).ConfigureAwait(false);

        Guid? created = null;
        var activate = target;
        if (!schemes.Contains(target))
        {
            created = await power.DuplicateAsync(target, ct).ConfigureAwait(false);
            activate = created.Value;
            ctx.Log.Debug("Power scheme {Target} is hidden; created {Created}", target, created);
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
        catch when (created is not null)
        {
            // The journal never learns about the duplicate, so remove it here rather than leak it.
            try
            {
                await power.DeleteAsync(created.Value, CancellationToken.None).ConfigureAwait(false);
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
        return captured with { Previous = Previous(previous, created) };
    }

    public async Task UndoAsync(JournalAction recorded, StepContext ctx, CancellationToken ct)
    {
        var previous = Guid.Parse(recorded.Previous.GetProperty("previous").GetString()!);
        await ctx.Services.Power.SetActiveAsync(previous, ct).ConfigureAwait(false);
        if (recorded.Previous.TryGetProperty("created", out var created) && created.ValueKind == JsonValueKind.String)
        {
            await ctx.Services.Power.DeleteAsync(Guid.Parse(created.GetString()!), ct).ConfigureAwait(false);
        }

        ctx.Log.Information("Power scheme restored to {Scheme}", previous);
    }

    /// <summary>The schemes created by unreverted journal entries: the source of the <c>createdSchemes</c> provider.</summary>
    internal static IReadOnlySet<Guid> CreatedSchemes(CleanupJournal? journal)
    {
        var created = new HashSet<Guid>();
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
                created.Add(scheme);
            }
        }

        return created;
    }

    private static JsonElement Previous(Guid previous, Guid? created) =>
        JsonSerializer.SerializeToElement(new { previous = previous.ToString(), created = created?.ToString() });

    private static Guid TargetGuid(PowerSchemeAction action) => action.Scheme switch
    {
        "balanced" => BuiltInPowerSchemes.Balanced,
        "highPerformance" => BuiltInPowerSchemes.HighPerformance,
        "ultimate" => BuiltInPowerSchemes.Ultimate,
        _ => throw new ArgumentException($"Unknown power scheme '{action.Scheme}'.", nameof(action)),
    };
}
