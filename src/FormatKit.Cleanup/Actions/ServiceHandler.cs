using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Actions;

internal sealed class ServiceHandler : IActionHandler
{
    public const string Type = "service";

    /// <summary>A service that does not exist has nothing to change, so it counts as applied.</summary>
    public Task<bool> IsAppliedAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var a = (ServiceAction)action;
        var current = ctx.Services.ServiceControl.GetStartType(a.Name);
        return Task.FromResult(current is null || current == a.StartType);
    }

    public Task<JournalAction> CaptureAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var a = (ServiceAction)action;
        var current = ctx.Services.ServiceControl.GetStartType(a.Name)
            ?? throw new InvalidOperationException($"Service '{a.Name}' does not exist.");
        ctx.Log.Debug("Service {Service} previous start type: {StartType}", a.Name, current);
        var previous = JsonSerializer.SerializeToElement(new { startType = current.ToString().ToLowerInvariant() });
        return Task.FromResult(new JournalAction(Type, a.Name, previous));
    }

    public async Task<JournalAction> ApplyAsync(CleanupAction action, JournalAction captured, StepContext ctx, CancellationToken ct)
    {
        var a = (ServiceAction)action;
        await ctx.Services.ServiceControl.SetStartTypeAsync(a.Name, a.StartType, ct).ConfigureAwait(false);
        var actual = ctx.Services.ServiceControl.GetStartType(a.Name);
        if (actual != a.StartType)
        {
            throw new InvalidOperationException($"Service '{a.Name}' start type is {actual?.ToString() ?? "unknown"}, expected {a.StartType}.");
        }

        ctx.Log.Information("Service {Service} start type set to {StartType}", a.Name, a.StartType);
        return captured;
    }

    public async Task UndoAsync(JournalAction recorded, StepContext ctx, CancellationToken ct)
    {
        var type = Enum.Parse<ServiceStartType>(recorded.Previous.GetProperty("startType").GetString()!, ignoreCase: true);
        await ctx.Services.ServiceControl.SetStartTypeAsync(recorded.Target, type, ct).ConfigureAwait(false);
        ctx.Log.Information("Service {Service} start type restored to {StartType}", recorded.Target, type);
    }
}
