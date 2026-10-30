using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Cleanup.Tests.Actions;

public class SoundSchemeHandlerTests
{
    private const string Schemes = @"HKCU\AppEvents\Schemes";
    private const string Asterisk = @"HKCU\AppEvents\Schemes\Apps\.Default\SystemAsterisk\.Current";
    private const string Exclamation = @"HKCU\AppEvents\Schemes\Apps\.Default\SystemExclamation\.Current";
    private const string Explorer = @"HKCU\AppEvents\Schemes\Apps\Explorer\Navigating\.Current";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly NamedAction Action = new("soundScheme");

    private static FakeRegistry Windows()
    {
        var registry = new FakeRegistry();
        registry.Set(Schemes, "", RegistryKind.String, ".Default");
        registry.Set(Asterisk, "", RegistryKind.String, @"C:\WINDOWS\media\Windows Background.wav");
        registry.Set(Exclamation, "", RegistryKind.ExpandString, @"%SystemRoot%\media\Windows Exclamation.wav");
        registry.Set(Explorer, "", RegistryKind.String, "");
        return registry;
    }

    [Fact]
    public async Task Apply_sets_none_and_clears_every_current_value()
    {
        var registry = Windows();
        var ctx = TestServices.Context(TestServices.Create(registry: registry));
        var handler = new SoundSchemeHandler();

        Assert.False(await handler.IsAppliedAsync(Action, ctx, Ct));
        var captured = await handler.CaptureAsync(Action, ctx, Ct);
        Assert.Equal("soundScheme", captured.Target);
        await handler.ApplyAsync(Action, captured, ctx, Ct);

        Assert.Equal(new RegistryValue(RegistryKind.String, ".None"), registry.Read(Schemes, ""));
        Assert.Equal(new RegistryValue(RegistryKind.String, ""), registry.Read(Asterisk, ""));
        Assert.Equal(new RegistryValue(RegistryKind.String, ""), registry.Read(Exclamation, ""));
        Assert.True(await handler.IsAppliedAsync(Action, ctx, Ct));
    }

    [Fact]
    public async Task Check_is_false_when_one_current_value_is_not_empty()
    {
        var registry = Windows();
        registry.Set(Schemes, "", RegistryKind.String, ".None");
        registry.Set(Asterisk, "", RegistryKind.String, "");
        registry.Set(Exclamation, "", RegistryKind.String, "x.wav");
        var ctx = TestServices.Context(TestServices.Create(registry: registry));

        Assert.False(await new SoundSchemeHandler().IsAppliedAsync(Action, ctx, Ct));
    }

    [Fact]
    public async Task Undo_restores_the_scheme_and_each_current_value()
    {
        var registry = Windows();
        var ctx = TestServices.Context(TestServices.Create(registry: registry));
        var handler = new SoundSchemeHandler();
        var captured = await handler.CaptureAsync(Action, ctx, Ct);
        var recorded = await handler.ApplyAsync(Action, captured, ctx, Ct);

        await handler.UndoAsync(recorded, ctx, Ct);

        Assert.Equal(new RegistryValue(RegistryKind.String, ".Default"), registry.Read(Schemes, ""));
        Assert.Equal(new RegistryValue(RegistryKind.String, @"C:\WINDOWS\media\Windows Background.wav"), registry.Read(Asterisk, ""));
        Assert.Equal(new RegistryValue(RegistryKind.ExpandString, @"%SystemRoot%\media\Windows Exclamation.wav"), registry.Read(Exclamation, ""));
        Assert.Equal(new RegistryValue(RegistryKind.String, ""), registry.Read(Explorer, ""));
    }
}
