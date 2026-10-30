using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core.Steps;

namespace FormatKit.Cleanup.Actions;

/// <summary>Sets one power setting (AC and optionally DC index) on the scheme that is active when the item is applied.</summary>
internal sealed class PowerSettingHandler : IActionHandler, IRevertCheckable
{
    public const string Type = "powerSetting";

    /// <summary>The check reads the active scheme, so it says nothing about a journaled scheme that is no longer active.</summary>
    public async Task<bool> CanCheckAsync(JournalAction recorded, StepContext ctx, CancellationToken ct)
    {
        var scheme = Guid.Parse(recorded.Previous.GetProperty("scheme").GetString()!);
        return await ctx.Services.Power.GetActiveAsync(ct).ConfigureAwait(false) == scheme;
    }

    /// <summary>A setting this machine does not have is not applied; capturing it reports the item as not present.</summary>
    public async Task<bool> IsAppliedAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var a = (PowerSettingAction)action;
        var power = ctx.Services.Power;
        var active = await power.GetActiveAsync(ct).ConfigureAwait(false);
        var current = await power.GetValueAsync(active, a.Subgroup, a.Setting, ct).ConfigureAwait(false);
        return current is { } value && Matches(a, value);
    }

    public async Task<JournalAction> CaptureAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var a = (PowerSettingAction)action;
        var power = ctx.Services.Power;
        var active = await power.GetActiveAsync(ct).ConfigureAwait(false);
        var current = await power.GetValueAsync(active, a.Subgroup, a.Setting, ct).ConfigureAwait(false)
            ?? throw new TargetNotPresentException($"{a.Subgroup}/{a.Setting}");

        ctx.Log.Debug("Power setting {Subgroup}/{Setting} on {Scheme} previous: AC {Ac}, DC {Dc}", a.Subgroup, a.Setting, active, current.Ac, current.Dc);
        return new JournalAction(Type, $"power:{a.Subgroup}/{a.Setting}",
            JsonSerializer.SerializeToElement(new { scheme = active.ToString(), ac = current.Ac, dc = current.Dc }));
    }

    public async Task<JournalAction> ApplyAsync(CleanupAction action, JournalAction captured, StepContext ctx, CancellationToken ct)
    {
        var a = (PowerSettingAction)action;
        var power = ctx.Services.Power;
        var scheme = Guid.Parse(captured.Previous.GetProperty("scheme").GetString()!);

        await power.SetValueAsync(scheme, a.Subgroup, a.Setting, a.Ac, a.Dc, ct).ConfigureAwait(false);
        var current = await power.GetValueAsync(scheme, a.Subgroup, a.Setting, ct).ConfigureAwait(false);
        if (current is not { } value || !Matches(a, value))
        {
            throw new InvalidOperationException($"Power setting {a.Subgroup}/{a.Setting} did not take effect on scheme {scheme}.");
        }

        ctx.Log.Information("Power setting {Subgroup}/{Setting} set on {Scheme}", a.Subgroup, a.Setting, scheme);
        return captured;
    }

    /// <summary>Restores both indexes on the journaled scheme; a scheme deleted since then is logged and skipped.</summary>
    public async Task UndoAsync(JournalAction recorded, StepContext ctx, CancellationToken ct)
    {
        var (subgroup, setting) = ParseTarget(recorded.Target);
        var scheme = Guid.Parse(recorded.Previous.GetProperty("scheme").GetString()!);
        var power = ctx.Services.Power;

        if (!(await power.ListAsync(ct).ConfigureAwait(false)).Contains(scheme))
        {
            ctx.Log.Warning("Power scheme {Scheme} no longer exists; power setting {Subgroup}/{Setting} not restored", scheme, subgroup, setting);
            return;
        }

        var ac = recorded.Previous.GetProperty("ac").GetInt32();
        var dc = recorded.Previous.GetProperty("dc").GetInt32();
        await power.SetValueAsync(scheme, subgroup, setting, ac, dc, ct).ConfigureAwait(false);
        ctx.Log.Information("Power setting {Subgroup}/{Setting} restored on {Scheme}", subgroup, setting, scheme);
    }

    private static bool Matches(PowerSettingAction action, (int Ac, int Dc) current) =>
        current.Ac == action.Ac && (action.Dc is not { } dc || current.Dc == dc);

    private static (string Subgroup, string Setting) ParseTarget(string target)
    {
        var parts = target.StartsWith("power:", StringComparison.Ordinal) ? target["power:".Length..].Split('/') : [];
        return parts.Length == 2
            ? (parts[0], parts[1])
            : throw new InvalidOperationException($"Invalid power setting journal target '{target}'.");
    }
}
