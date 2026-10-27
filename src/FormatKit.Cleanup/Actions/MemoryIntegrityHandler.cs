using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Actions;

/// <summary>Turns off Memory Integrity (HVCI) and virtualization-based security through two registry values.</summary>
internal sealed class MemoryIntegrityHandler : IActionHandler
{
    public const string Name = "memoryIntegrity";

    private const string HvciPath = @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    private const string HvciName = "Enabled";
    private const string VbsPath = @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard";
    private const string VbsName = "EnableVirtualizationBasedSecurity";
    private static readonly RegistryValue Off = new(RegistryKind.Dword, "0");

    public Task<bool> IsAppliedAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var registry = ctx.Services.Registry;
        return Task.FromResult(registry.Read(HvciPath, HvciName) == Off && registry.Read(VbsPath, VbsName) == Off);
    }

    public Task<JournalAction> CaptureAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var registry = ctx.Services.Registry;
        var hvci = RegistryHandler.CapturePrevious(registry, HvciPath, HvciName);
        var vbs = RegistryHandler.CapturePrevious(registry, VbsPath, VbsName);
        ctx.Log.Debug("Memory integrity previous state: hvci={Hvci}, vbs={Vbs}", hvci, vbs);
        return Task.FromResult(new JournalAction(Name, Name, JsonSerializer.SerializeToElement(new { hvci, vbs })));
    }

    public Task<JournalAction> ApplyAsync(CleanupAction action, JournalAction captured, StepContext ctx, CancellationToken ct)
    {
        RegistryHandler.WriteAndVerify(ctx.Services.Registry, HvciPath, HvciName, Off);
        RegistryHandler.WriteAndVerify(ctx.Services.Registry, VbsPath, VbsName, Off);
        ctx.Log.Debug("Memory integrity new state: hvci={Hvci}, vbs={Vbs}", Off, Off);
        ctx.Log.Information("Memory integrity and virtualization-based security disabled");
        return Task.FromResult(captured);
    }

    public Task UndoAsync(JournalAction recorded, StepContext ctx, CancellationToken ct)
    {
        RegistryHandler.Restore(ctx.Services.Registry, HvciPath, HvciName, recorded.Previous.GetProperty("hvci"));
        RegistryHandler.Restore(ctx.Services.Registry, VbsPath, VbsName, recorded.Previous.GetProperty("vbs"));
        ctx.Log.Information("Memory integrity settings restored");
        return Task.CompletedTask;
    }
}
