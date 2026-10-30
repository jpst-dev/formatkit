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
        var effective = IsEffectivelyEnabled(ctx.Services.Registry);
        ctx.Log.Debug("Hibernation previous state: {Enabled}, effectively enabled {Effective}", enabled, effective);
        return Task.FromResult(new JournalAction(Name, Name, JsonSerializer.SerializeToElement(new { enabled, effective })));
    }

    /// <summary>True unless <c>HibernateEnabled</c> (or, when absent, <c>HibernateEnabledDefault</c>) is a DWORD 0.</summary>
    private static bool IsEffectivelyEnabled(IRegistry registry)
    {
        var effective = registry.Read(PowerPath, EnabledName) ?? registry.Read(PowerPath, DefaultName);
        return effective is not { Kind: RegistryKind.Dword, Data: "0" };
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

    /// <summary>
    /// Runs <c>powercfg /hibernate on</c> when hibernation was effectively enabled (it recreates the hibernation
    /// file), then puts <c>HibernateEnabled</c> back as it was: deleted when it was absent, so the default rules again.
    /// A powercfg failure (for example firmware without hibernation) is logged and does not stop the undo.
    /// </summary>
    public async Task UndoAsync(JournalAction recorded, StepContext ctx, CancellationToken ct)
    {
        var registry = ctx.Services.Registry;
        var enabled = recorded.Previous.GetProperty("enabled");
        var wasAbsent = enabled.TryGetProperty("absent", out var absent) && absent.GetBoolean();
        var wasOn = recorded.Previous.TryGetProperty("effective", out var effective)
            ? effective.GetBoolean()
            : wasAbsent
                ? registry.Read(PowerPath, DefaultName) is not { Kind: RegistryKind.Dword, Data: "0" }
                : enabled.GetProperty("data").GetString() != "0";

        if (wasOn)
        {
            try
            {
                await PowerCfgAsync(ctx, "on", ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ctx.Log.Warning(ex, "Could not turn hibernation back on; run powercfg /hibernate on yourself if you need it");
            }
        }

        RegistryHandler.Restore(registry, PowerPath, EnabledName, enabled);
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
