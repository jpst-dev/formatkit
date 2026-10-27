using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Actions;

internal sealed class OptionalFeatureHandler : IActionHandler
{
    public const string Type = "optionalFeature";

    public async Task<bool> IsAppliedAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var a = (OptionalFeatureAction)action;
        return await ctx.Services.Features.GetStateAsync(a.Name, ct).ConfigureAwait(false) == a.State;
    }

    public async Task<JournalAction> CaptureAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var a = (OptionalFeatureAction)action;
        var state = await ctx.Services.Features.GetStateAsync(a.Name, ct).ConfigureAwait(false);
        if (state == OptionalFeatureState.Absent)
        {
            throw new InvalidOperationException($"Optional feature '{a.Name}' is not present on this system.");
        }

        ctx.Log.Debug("Optional feature {Feature} previous state: {State}", a.Name, state);
        return new JournalAction(Type, a.Name, JsonSerializer.SerializeToElement(new { state = state.ToString().ToLowerInvariant() }));
    }

    public async Task<JournalAction> ApplyAsync(CleanupAction action, JournalAction captured, StepContext ctx, CancellationToken ct)
    {
        var a = (OptionalFeatureAction)action;
        var restart = await ctx.Services.Features.SetStateAsync(a.Name, a.State == OptionalFeatureState.Enabled, ct).ConfigureAwait(false);
        var actual = await ctx.Services.Features.GetStateAsync(a.Name, ct).ConfigureAwait(false);
        if (actual != a.State)
        {
            throw new InvalidOperationException($"Optional feature '{a.Name}' is {actual}, expected {a.State}.");
        }

        ctx.Log.Information("Optional feature {Feature} set to {State} (restart needed: {Restart})", a.Name, a.State, restart);
        return captured;
    }

    public async Task UndoAsync(JournalAction recorded, StepContext ctx, CancellationToken ct)
    {
        var state = Enum.Parse<OptionalFeatureState>(recorded.Previous.GetProperty("state").GetString()!, ignoreCase: true);
        await ctx.Services.Features.SetStateAsync(recorded.Target, state == OptionalFeatureState.Enabled, ct).ConfigureAwait(false);
        ctx.Log.Information("Optional feature {Feature} restored to {State}", recorded.Target, state);
    }
}
