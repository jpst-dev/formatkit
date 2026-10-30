using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Actions;

/// <summary>
/// Disables a startup program the way Task Manager does: it marks the StartupApproved entry as disabled.
/// The Run value itself is never written or deleted.
/// </summary>
internal sealed class StartupItemHandler : IActionHandler
{
    public const string Type = "startupItem";

    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>Twelve bytes: <c>03</c> marks the entry disabled, the rest is the (unused) disable timestamp.</summary>
    private const string DisabledData = "030000000000000000000000";

    /// <summary>
    /// A missing Run value is not applied, so capture reports the item as not present: the program may not have
    /// registered itself yet (OneDrive's first-logon setup), and a later run then picks it up.
    /// </summary>
    public Task<bool> IsAppliedAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var a = (StartupItemAction)action;
        var registry = ctx.Services.Registry;
        if (registry.Read(Root(a.Scope) + RunSubKey, a.Name) is null)
        {
            return Task.FromResult(false);
        }

        var approval = registry.Read(Root(a.Scope) + ApprovedSubKey, a.Name);
        return Task.FromResult(approval is { Kind: RegistryKind.Binary } && approval.Data.StartsWith("03", StringComparison.OrdinalIgnoreCase));
    }

    public Task<JournalAction> CaptureAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var a = (StartupItemAction)action;
        var registry = ctx.Services.Registry;
        if (registry.Read(Root(a.Scope) + RunSubKey, a.Name) is null)
        {
            throw new TargetNotPresentException(a.Name);
        }

        var previous = RegistryHandler.CapturePrevious(registry, Root(a.Scope) + ApprovedSubKey, a.Name);
        ctx.Log.Debug("Startup item {Name} ({Scope}) previous approval: {Previous}", a.Name, a.Scope, previous);
        return Task.FromResult(new JournalAction(Type, Target(a.Scope, a.Name), previous));
    }

    public Task<JournalAction> ApplyAsync(CleanupAction action, JournalAction captured, StepContext ctx, CancellationToken ct)
    {
        var a = (StartupItemAction)action;
        RegistryHandler.WriteAndVerify(ctx.Services.Registry, Root(a.Scope) + ApprovedSubKey, a.Name, new RegistryValue(RegistryKind.Binary, DisabledData));
        ctx.Log.Information("Startup item {Name} ({Scope}) disabled", a.Name, a.Scope);
        return Task.FromResult(captured);
    }

    public Task UndoAsync(JournalAction recorded, StepContext ctx, CancellationToken ct)
    {
        var parts = recorded.Target.Split(':', 3);
        if (parts.Length != 3 || parts[0] != "startup" || !Enum.TryParse<ItemScope>(parts[1], ignoreCase: true, out var scope))
        {
            throw new InvalidOperationException($"Invalid startup item journal target '{recorded.Target}'.");
        }

        RegistryHandler.Restore(ctx.Services.Registry, Root(scope) + ApprovedSubKey, parts[2], recorded.Previous);
        ctx.Log.Information("Startup item {Name} ({Scope}) approval restored", parts[2], scope);
        return Task.CompletedTask;
    }

    private static string Root(ItemScope scope) => scope == ItemScope.User ? @"HKCU\" : @"HKLM\";

    private static string Target(ItemScope scope, string name) => $"startup:{scope.ToString().ToLowerInvariant()}:{name}";
}
