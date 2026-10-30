using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Cleanup.Tests.Actions;

public class StartupItemHandlerTests
{
    private const string UserRun = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string UserApproved = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string MachineRun = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string MachineApproved = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string Disabled = "030000000000000000000000";
    private const string Enabled = "020000000000000000000000";
    private const string RunCommand = "\"C:\\OneDrive.exe\" /background";
    private static readonly StartupItemAction User = new("OneDrive", ItemScope.User);
    private static readonly StartupItemAction Machine = new("OneDrive", ItemScope.Machine);
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class RecordingRegistry : FakeRegistry
    {
        public List<string> Writes { get; } = [];

        public override void Write(string path, string name, RegistryValue value)
        {
            Writes.Add(path + "|" + name);
            base.Write(path, name, value);
        }
    }

    private static (StartupItemHandler Handler, RecordingRegistry Registry, Core.Steps.StepContext Ctx) Create(string? run = RunCommand, string? approved = null, bool machine = false)
    {
        var registry = new RecordingRegistry();
        if (run is not null)
            registry.Set(machine ? MachineRun : UserRun, "OneDrive", RegistryKind.String, run);
        if (approved is not null)
            registry.Set(machine ? MachineApproved : UserApproved, "OneDrive", RegistryKind.Binary, approved);
        return (new StartupItemHandler(), registry, TestServices.Context(TestServices.Create(registry: registry)));
    }

    [Fact]
    public async Task Missing_run_value_is_not_applied_so_capture_reports_it_not_present()
    {
        var (handler, _, ctx) = Create(run: null);
        Assert.False(await handler.IsAppliedAsync(User, ctx, Ct));
    }

    [Fact]
    public async Task Missing_run_value_capture_reports_not_present_and_writes_nothing()
    {
        var (handler, registry, ctx) = Create(run: null);
        await Assert.ThrowsAsync<TargetNotPresentException>(() => handler.CaptureAsync(User, ctx, Ct));
        Assert.Empty(registry.Writes);
        Assert.Null(registry.Read(UserApproved, "OneDrive"));
    }

    [Fact]
    public async Task Disabled_approval_is_applied()
    {
        var (handler, _, ctx) = Create(approved: Disabled);
        Assert.True(await handler.IsAppliedAsync(User, ctx, Ct));
    }

    [Theory]
    [InlineData(Enabled)]
    [InlineData(null)]
    public async Task Enabled_or_absent_approval_is_not_applied(string? approved)
    {
        var (handler, _, ctx) = Create(approved: approved);
        Assert.False(await handler.IsAppliedAsync(User, ctx, Ct));
    }

    [Fact]
    public async Task Apply_writes_disabled_bytes_and_journals_previous_bytes()
    {
        var (handler, registry, ctx) = Create(approved: Enabled);

        var captured = await handler.CaptureAsync(User, ctx, Ct);
        Assert.Equal("startup:user:OneDrive", captured.Target);
        Assert.Equal(Enabled, captured.Previous.GetProperty("data").GetString());

        await handler.ApplyAsync(User, captured, ctx, Ct);

        var value = registry.Read(UserApproved, "OneDrive")!;
        Assert.Equal(RegistryKind.Binary, value.Kind);
        Assert.Equal(Disabled, value.Data);
    }

    [Fact]
    public async Task Undo_restores_previous_bytes()
    {
        var (handler, registry, ctx) = Create(approved: Enabled);
        var recorded = await handler.ApplyAsync(User, await handler.CaptureAsync(User, ctx, Ct), ctx, Ct);

        await handler.UndoAsync(recorded, ctx, Ct);

        Assert.Equal(Enabled, registry.Read(UserApproved, "OneDrive")!.Data);
    }

    [Fact]
    public async Task Absent_approval_is_journaled_as_absent_and_undo_deletes_it()
    {
        var (handler, registry, ctx) = Create(approved: null);
        var captured = await handler.CaptureAsync(User, ctx, Ct);
        Assert.True(captured.Previous.GetProperty("absent").GetBoolean());
        var recorded = await handler.ApplyAsync(User, captured, ctx, Ct);
        Assert.Equal(Disabled, registry.Read(UserApproved, "OneDrive")!.Data);

        await handler.UndoAsync(recorded, ctx, Ct);

        Assert.Null(registry.Read(UserApproved, "OneDrive"));
    }

    [Fact]
    public async Task Machine_scope_uses_HKLM_and_never_touches_the_run_value()
    {
        var (handler, registry, ctx) = Create(approved: Enabled, machine: true);
        registry.Writes.Clear();
        var recorded = await handler.ApplyAsync(Machine, await handler.CaptureAsync(Machine, ctx, Ct), ctx, Ct);
        await handler.UndoAsync(recorded, ctx, Ct);

        Assert.Equal("startup:machine:OneDrive", recorded.Target);
        Assert.NotEmpty(registry.Writes);
        Assert.All(registry.Writes, w => Assert.Equal(MachineApproved + "|OneDrive", w));
        Assert.Equal(RunCommand, registry.Read(MachineRun, "OneDrive")!.Data);
    }

    [Fact]
    public async Task Run_value_is_never_written_or_deleted()
    {
        var (handler, registry, ctx) = Create(approved: null);
        var recorded = await handler.ApplyAsync(User, await handler.CaptureAsync(User, ctx, Ct), ctx, Ct);
        await handler.UndoAsync(recorded, ctx, Ct);

        Assert.DoesNotContain(registry.Writes, w => w.StartsWith(UserRun + "|", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(RunCommand, registry.Read(UserRun, "OneDrive")!.Data);
    }
}
