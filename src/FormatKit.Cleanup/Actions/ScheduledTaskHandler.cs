using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core.Steps;

namespace FormatKit.Cleanup.Actions;

internal sealed class ScheduledTaskHandler : IActionHandler
{
    public const string Type = "scheduledTask";

    /// <summary>A missing task is not applied: capture then reports it as "not present".</summary>
    public async Task<bool> IsAppliedAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var a = (ScheduledTaskAction)action;
        var current = await ctx.Services.TaskControl.IsEnabledAsync(a.Path, a.Name, ct).ConfigureAwait(false);
        return current == a.Enabled;
    }

    public async Task<JournalAction> CaptureAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var a = (ScheduledTaskAction)action;
        var current = await ctx.Services.TaskControl.IsEnabledAsync(a.Path, a.Name, ct).ConfigureAwait(false)
            ?? throw new TargetNotPresentException(a.Path + a.Name);
        ctx.Log.Debug("Scheduled task {Task} previous enabled state: {Enabled}", a.Path + a.Name, current);
        return new JournalAction(Type, a.Path + a.Name, JsonSerializer.SerializeToElement(new { path = a.Path, name = a.Name, enabled = current }));
    }

    public async Task<JournalAction> ApplyAsync(CleanupAction action, JournalAction captured, StepContext ctx, CancellationToken ct)
    {
        var a = (ScheduledTaskAction)action;
        await ctx.Services.TaskControl.SetEnabledAsync(a.Path, a.Name, a.Enabled, ct).ConfigureAwait(false);
        var actual = await ctx.Services.TaskControl.IsEnabledAsync(a.Path, a.Name, ct).ConfigureAwait(false);
        if (actual != a.Enabled)
        {
            throw new InvalidOperationException($"Scheduled task '{a.Path}{a.Name}' enabled is {actual?.ToString() ?? "unknown"}, expected {a.Enabled}.");
        }

        ctx.Log.Information("Scheduled task {Task} enabled set to {Enabled}", a.Path + a.Name, a.Enabled);
        return captured;
    }

    public async Task UndoAsync(JournalAction recorded, StepContext ctx, CancellationToken ct)
    {
        var path = recorded.Previous.GetProperty("path").GetString()!;
        var name = recorded.Previous.GetProperty("name").GetString()!;
        var enabled = recorded.Previous.GetProperty("enabled").GetBoolean();
        var current = await ctx.Services.TaskControl.IsEnabledAsync(path, name, ct).ConfigureAwait(false);
        if (current is null)
        {
            ctx.Log.Information("Scheduled task {Task} no longer exists; nothing to restore", recorded.Target);
            return;
        }

        if (current != enabled)
        {
            await ctx.Services.TaskControl.SetEnabledAsync(path, name, enabled, ct).ConfigureAwait(false);
        }

        ctx.Log.Information("Scheduled task {Task} enabled restored to {Enabled}", recorded.Target, enabled);
    }
}
