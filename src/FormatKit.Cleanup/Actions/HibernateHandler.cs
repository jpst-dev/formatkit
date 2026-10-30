using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Actions;

/// <summary>Turns hibernation off with <c>powercfg /hibernate off</c>.</summary>
internal sealed class HibernateHandler : IActionHandler
{
    public const string Name = "hibernate";

    private const string PowerPath = @"HKLM\SYSTEM\CurrentControlSet\Control\Power";
    private const string EnabledName = "HibernateEnabled";
    private const string DefaultName = "HibernateEnabledDefault";

    public Task<bool> IsAppliedAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var registry = ctx.Services.Registry;
        var effective = registry.Read(PowerPath, EnabledName) ?? registry.Read(PowerPath, DefaultName);
        return Task.FromResult(effective is { Kind: RegistryKind.Dword, Data: "0" });
    }

    public Task<JournalAction> CaptureAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var enabled = RegistryHandler.CapturePrevious(ctx.Services.Registry, PowerPath, EnabledName);
        ctx.Log.Debug("Hibernation previous state: {Enabled}", enabled);
        return Task.FromResult(new JournalAction(Name, Name, JsonSerializer.SerializeToElement(new { enabled })));
    }

    public async Task<JournalAction> ApplyAsync(CleanupAction action, JournalAction captured, StepContext ctx, CancellationToken ct)
    {
        await PowerCfgAsync(ctx, "off", ct).ConfigureAwait(false);
        if (!await IsAppliedAsync(action, ctx, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Hibernation is still enabled after powercfg /hibernate off.");
        }

        ctx.Log.Information("Hibernation turned off");
        return captured;
    }

    /// <summary>Deletes the value when it was absent; turns hibernation back on only when it was 1.</summary>
    public async Task UndoAsync(JournalAction recorded, StepContext ctx, CancellationToken ct)
    {
        var enabled = recorded.Previous.GetProperty("enabled");
        if (enabled.TryGetProperty("absent", out var absent) && absent.GetBoolean())
        {
            ctx.Services.Registry.Delete(PowerPath, EnabledName);
        }
        else if (enabled.GetProperty("data").GetString() == "1")
        {
            await PowerCfgAsync(ctx, "on", ct).ConfigureAwait(false);
        }

        ctx.Log.Information("Hibernation state restored");
    }

    private static async Task PowerCfgAsync(StepContext ctx, string state, CancellationToken ct)
    {
        var result = await ctx.Services.Processes.RunAsync(
            new ProcessSpec("powercfg.exe", ["/hibernate", state], Timeouts.Query), ct).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException($"powercfg /hibernate {state} failed (exit {result.ExitCode}): {result.StdErr.Trim()}");
        }
    }
}
