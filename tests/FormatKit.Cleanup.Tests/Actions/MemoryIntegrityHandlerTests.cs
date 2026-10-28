using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Cleanup.Tests.Actions;

public class MemoryIntegrityHandlerTests
{
    private const string HvciPath = @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    private const string VbsPath = @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly NamedAction Action = new("memoryIntegrity");

    [Fact]
    public async Task Apply_writes_both_values()
    {
        var registry = new FakeRegistry();
        registry.Set(HvciPath, "Enabled", RegistryKind.Dword, "1");
        var ctx = TestServices.Context(TestServices.Create(registry: registry));
        var handler = new MemoryIntegrityHandler();

        Assert.False(await handler.IsAppliedAsync(Action, ctx, Ct));
        var captured = await handler.CaptureAsync(Action, ctx, Ct);
        await handler.ApplyAsync(Action, captured, ctx, Ct);

        Assert.Equal(new RegistryValue(RegistryKind.Dword, "0"), registry.Read(HvciPath, "Enabled"));
        Assert.Equal(new RegistryValue(RegistryKind.Dword, "0"), registry.Read(VbsPath, "EnableVirtualizationBasedSecurity"));
        Assert.True(await handler.IsAppliedAsync(Action, ctx, Ct));
    }

    [Fact]
    public async Task Undo_restores_both()
    {
        var registry = new FakeRegistry();
        registry.Set(HvciPath, "Enabled", RegistryKind.Dword, "1");
        var ctx = TestServices.Context(TestServices.Create(registry: registry));
        var handler = new MemoryIntegrityHandler();
        var captured = await handler.CaptureAsync(Action, ctx, Ct);
        var recorded = await handler.ApplyAsync(Action, captured, ctx, Ct);

        await handler.UndoAsync(recorded, ctx, Ct);

        Assert.Equal(new RegistryValue(RegistryKind.Dword, "1"), registry.Read(HvciPath, "Enabled"));
        Assert.Null(registry.Read(VbsPath, "EnableVirtualizationBasedSecurity"));
    }

    [Fact]
    public void ActionHandlers_dispatch_by_type_and_handler_name()
    {
        var handlers = new ActionHandlers(() => new Dictionary<Guid, Guid?>());

        Assert.IsType<MemoryIntegrityHandler>(handlers.For(Action));
        Assert.IsType<MemoryIntegrityHandler>(handlers.For(new FormatKit.Cleanup.Journal.JournalAction("memoryIntegrity", "memoryIntegrity", default)));
        Assert.IsType<PowerSchemeHandler>(handlers.For(new PowerSchemeAction("balanced")));
        Assert.IsType<AppxHandler>(handlers.For(new FormatKit.Cleanup.Journal.JournalAction("appx", "x", default)));
        Assert.Throws<ArgumentException>(() => handlers.For(new NamedAction("nope")));
    }
}
