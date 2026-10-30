using System.Text.Json;
using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Cleanup.Tests.Actions;

public class RegistryHandlerTests
{
    private const string Path = @"HKCU\Control Panel\Mouse";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly RegistryHandler Handler = new();

    private static JsonElement Num(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static RegistryAction Dword(string name, string value) =>
        new(Path, name, RegistryKind.Dword, Num(value), false);

    private static RegistryAction Str(string name, string value) =>
        new(Path, name, RegistryKind.String, JsonDocument.Parse($"\"{value}\"").RootElement.Clone(), false);

    [Fact]
    public async Task Applied_when_value_matches()
    {
        var registry = new FakeRegistry();
        registry.Set(Path, "A", RegistryKind.Dword, "1");
        var ctx = TestServices.Context(TestServices.Create(registry: registry));

        Assert.True(await Handler.IsAppliedAsync(Dword("A", "1"), ctx, Ct));
        Assert.False(await Handler.IsAppliedAsync(Dword("A", "2"), ctx, Ct));
        Assert.False(await Handler.IsAppliedAsync(Dword("Missing", "1"), ctx, Ct));
    }

    [Fact]
    public async Task Registry_action_with_missing_ifFileExists_is_not_applicable()
    {
        var registry = new FakeRegistry();
        var files = new InMemoryFileSystem();
        var ctx = TestServices.Context(TestServices.Create(registry: registry, files: files));
        var action = Dword("A", "1") with { IfFileExists = @"%SystemRoot%\System32\sudo.exe" };

        await Assert.ThrowsAsync<TargetNotPresentException>(() => Handler.CaptureAsync(action, ctx, Ct));
        Assert.Null(registry.Read(Path, "A"));
    }

    [Fact]
    public async Task Registry_action_with_existing_ifFileExists_is_captured()
    {
        var files = new InMemoryFileSystem();
        files.Seed(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\sudo.exe"), "");
        var ctx = TestServices.Context(TestServices.Create(files: files));
        var action = Dword("A", "1") with { IfFileExists = @"%SystemRoot%\System32\sudo.exe" };

        var captured = await Handler.CaptureAsync(action, ctx, Ct);

        Assert.Equal(Path + "|A", captured.Target);
    }

    [Fact]
    public async Task Capture_records_absent()
    {
        var ctx = TestServices.Context(TestServices.Create());

        var captured = await Handler.CaptureAsync(Dword("A", "1"), ctx, Ct);

        Assert.Equal("registry", captured.Type);
        Assert.Equal(Path + "|A", captured.Target);
        Assert.True(captured.Previous.GetProperty("absent").GetBoolean());
    }

    [Fact]
    public async Task Undo_of_absent_deletes()
    {
        var registry = new FakeRegistry();
        var ctx = TestServices.Context(TestServices.Create(registry: registry));
        var action = Dword("A", "1");

        var captured = await Handler.CaptureAsync(action, ctx, Ct);
        var recorded = await Handler.ApplyAsync(action, captured, ctx, Ct);
        Assert.NotNull(registry.Read(Path, "A"));

        await Handler.UndoAsync(recorded, ctx, Ct);

        Assert.Null(registry.Read(Path, "A"));
    }

    [Fact]
    public async Task Undo_restores_string_kind()
    {
        var registry = new FakeRegistry();
        registry.Set(Path, "MouseSpeed", RegistryKind.String, "1");
        var ctx = TestServices.Context(TestServices.Create(registry: registry));
        var action = Str("MouseSpeed", "0");

        var captured = await Handler.CaptureAsync(action, ctx, Ct);
        var recorded = await Handler.ApplyAsync(action, captured, ctx, Ct);
        Assert.Equal(new RegistryValue(RegistryKind.String, "0"), registry.Read(Path, "MouseSpeed"));

        await Handler.UndoAsync(recorded, ctx, Ct);

        Assert.Equal(new RegistryValue(RegistryKind.String, "1"), registry.Read(Path, "MouseSpeed"));
    }

    [Fact]
    public async Task Dword_max_round_trips_through_journal_json()
    {
        var registry = new FakeRegistry();
        registry.Set(Path, "A", RegistryKind.Dword, "4294967295");
        var ctx = TestServices.Context(TestServices.Create(registry: registry));
        var action = Dword("A", "0");

        var captured = await Handler.CaptureAsync(action, ctx, Ct);
        var recorded = await Handler.ApplyAsync(action, captured, ctx, Ct);
        var roundTripped = JsonSerializer.Deserialize<JournalAction>(JsonSerializer.Serialize(recorded))!;

        await Handler.UndoAsync(roundTripped, ctx, Ct);

        Assert.Equal(new RegistryValue(RegistryKind.Dword, "4294967295"), registry.Read(Path, "A"));
    }

    [Fact]
    public async Task Verify_mismatch_throws()
    {
        var ctx = TestServices.Context(TestServices.Create(registry: new IgnoringRegistry()));
        var action = Dword("A", "1");
        var captured = await Handler.CaptureAsync(action, ctx, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler.ApplyAsync(action, captured, ctx, Ct));
    }

    [Fact]
    public async Task Delete_action_applied_when_absent()
    {
        var registry = new FakeRegistry();
        var ctx = TestServices.Context(TestServices.Create(registry: registry));
        var action = new RegistryAction(Path, "A", null, null, true);

        Assert.True(await Handler.IsAppliedAsync(action, ctx, Ct));

        registry.Set(Path, "A", RegistryKind.Dword, "1");
        Assert.False(await Handler.IsAppliedAsync(action, ctx, Ct));

        var captured = await Handler.CaptureAsync(action, ctx, Ct);
        var recorded = await Handler.ApplyAsync(action, captured, ctx, Ct);
        Assert.Null(registry.Read(Path, "A"));

        await Handler.UndoAsync(recorded, ctx, Ct);
        Assert.Equal(new RegistryValue(RegistryKind.Dword, "1"), registry.Read(Path, "A"));
    }

    private sealed class IgnoringRegistry : FakeRegistry
    {
        public override void Write(string path, string name, RegistryValue value)
        {
        }
    }

    private static RegistryAction Bin(string name, string hex) =>
        new(Path, name, RegistryKind.Binary, JsonDocument.Parse($"\"{hex}\"").RootElement.Clone(), false);

    [Fact]
    public async Task Binary_value_is_applied_and_undone()
    {
        var registry = new FakeRegistry();
        registry.Set(Path, "B", RegistryKind.Binary, "0200000000000000");
        var ctx = TestServices.Context(TestServices.Create(registry: registry));
        var action = Bin("B", "0300000000000000");

        var captured = await Handler.CaptureAsync(action, ctx, Ct);
        var applied = await Handler.ApplyAsync(action, captured, ctx, Ct);

        Assert.Equal(new RegistryValue(RegistryKind.Binary, "0300000000000000"), registry.Read(Path, "B"));

        await Handler.UndoAsync(applied, ctx, Ct);

        Assert.Equal(new RegistryValue(RegistryKind.Binary, "0200000000000000"), registry.Read(Path, "B"));
    }

    [Fact]
    public async Task Binary_value_is_written_as_lowercase_hex()
    {
        var registry = new FakeRegistry();
        var ctx = TestServices.Context(TestServices.Create(registry: registry));
        var action = Bin("B", "0A0B");

        await Handler.ApplyAsync(action, await Handler.CaptureAsync(action, ctx, Ct), ctx, Ct);

        Assert.Equal("0a0b", registry.Read(Path, "B")!.Data);
    }

    [Fact]
    public async Task Binary_comparison_ignores_hex_case()
    {
        var registry = new FakeRegistry();
        registry.Set(Path, "B", RegistryKind.Binary, "0a0b");
        var ctx = TestServices.Context(TestServices.Create(registry: registry));

        Assert.True(await Handler.IsAppliedAsync(Bin("B", "0A0B"), ctx, Ct));
        Assert.False(await Handler.IsAppliedAsync(Bin("B", "0A0C"), ctx, Ct));
    }

    [Fact]
    public async Task Default_value_is_written_and_undone_to_absent()
    {
        var registry = new FakeRegistry();
        var ctx = TestServices.Context(TestServices.Create(registry: registry));
        var action = Str("", "");

        var captured = await Handler.CaptureAsync(action, ctx, Ct);
        var applied = await Handler.ApplyAsync(action, captured, ctx, Ct);

        Assert.Equal(new RegistryValue(RegistryKind.String, ""), registry.Read(Path, ""));
        Assert.Equal(Path + "|", applied.Target);

        await Handler.UndoAsync(applied, ctx, Ct);

        Assert.Null(registry.Read(Path, ""));
    }
}
