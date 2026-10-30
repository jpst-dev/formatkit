using FormatKit.Cleanup.Actions;
using FormatKit.Cleanup.Catalog;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Cleanup.Tests.Actions;

public class KeyboardLayoutHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly NamedAction Action = new("keyboardLayout");

    // Shape printed by Windows PowerShell 5.1 on the pt-BR VM for ConvertTo-Json -InputObject @(Get-WinUserLanguageList).
    private const string PtBrDefault = """
        [ { "LanguageTag": "pt-BR", "Autonym": "Português (Brasil)", "InputMethodTips": [ "0416:00010416", "0416:00000416" ], "Spellchecking": true } ]
        """;

    private const string PtBrUsIntl = """[ { "LanguageTag": "pt-BR", "InputMethodTips": [ "0416:00020409" ] } ]""";

    private const string Mixed = """
        [ { "LanguageTag": "pt-BR", "InputMethodTips": [ "0416:00010416", "0416:00020409" ] },
          { "LanguageTag": "en-US", "InputMethodTips": [ "0409:00000409" ] } ]
        """;

    private static ProcessResult Ok(string stdout = "") => new(0, stdout, "", TimeSpan.Zero, false);

    private static (KeyboardLayoutHandler Handler, FakeProcessRunner Runner, StepContext Ctx) Setup()
    {
        var runner = new FakeProcessRunner();
        return (new KeyboardLayoutHandler(), runner, TestServices.Context(TestServices.Create(processes: runner)));
    }

    private static string Script(ProcessSpec spec)
    {
        Assert.Equal("powershell.exe", spec.FileName);
        Assert.Equal(["-NoProfile", "-NonInteractive", "-Command"], spec.Arguments.Take(3));
        return spec.Arguments[3];
    }

    [Fact]
    public async Task Check_is_true_only_for_ptbr_first_with_exactly_us_international()
    {
        var (handler, runner, ctx) = Setup();
        runner.Enqueue("powershell.exe", Ok(PtBrUsIntl));
        runner.Enqueue("powershell.exe", Ok(PtBrDefault));
        runner.Enqueue("powershell.exe", Ok(Mixed));
        runner.Enqueue("powershell.exe", Ok("""{ "LanguageTag": "pt-BR", "InputMethodTips": "0416:00020409" }"""));

        Assert.True(await handler.IsAppliedAsync(Action, ctx, Ct));
        Assert.False(await handler.IsAppliedAsync(Action, ctx, Ct));
        Assert.False(await handler.IsAppliedAsync(Action, ctx, Ct));
        Assert.True(await handler.IsAppliedAsync(Action, ctx, Ct));
        Assert.Contains("@(Get-WinUserLanguageList ", Script(runner.Calls[0]));
        Assert.Contains("ConvertTo-Json -InputObject", Script(runner.Calls[0]));
    }

    [Fact]
    public async Task Apply_sets_ptbr_to_us_international_and_keeps_other_languages_after_it()
    {
        var (handler, runner, ctx) = Setup();
        runner.Enqueue("powershell.exe", Ok("""
            [ { "LanguageTag": "en-US", "InputMethodTips": [ "0409:00000409" ] },
              { "LanguageTag": "pt-BR", "InputMethodTips": [ "0416:00010416", "0416:00000416" ] } ]
            """));
        var captured = await handler.CaptureAsync(Action, ctx, Ct);
        Assert.Equal("keyboardLayout", captured.Type);
        Assert.Equal("keyboardLayout", captured.Target);
        runner.Enqueue("powershell.exe", Ok());
        runner.Enqueue("powershell.exe", Ok("""
            [ { "LanguageTag": "pt-BR", "InputMethodTips": [ "0416:00020409" ] },
              { "LanguageTag": "en-US", "InputMethodTips": [ "0409:00000409" ] } ]
            """));

        await handler.ApplyAsync(Action, captured, ctx, Ct);

        var script = Script(runner.Calls[1]);
        Assert.Contains("Set-WinUserLanguageList", script);
        Assert.Contains("-Force", script);
        Assert.Contains("New-WinUserLanguageList 'pt-BR'", script);
        Assert.Contains("'0416:00020409'", script);
        Assert.Contains("'en-US'", script);
        Assert.Contains("'0409:00000409'", script);
        Assert.DoesNotContain("0416:00010416", script);
        Assert.True(script.IndexOf("'pt-BR'", StringComparison.Ordinal) < script.IndexOf("'en-US'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_replaces_abnt2_and_us_international_with_us_international_only_and_keeps_en_us()
    {
        var (handler, runner, ctx) = Setup();
        runner.Enqueue("powershell.exe", Ok("""
            [ { "LanguageTag": "pt-BR", "InputMethodTips": [ "0416:00010416", "0416:00020409" ] },
              { "LanguageTag": "en-US", "InputMethodTips": [ "0409:00000409" ] } ]
            """));
        var captured = await handler.CaptureAsync(Action, ctx, Ct);
        runner.Enqueue("powershell.exe", Ok());
        runner.Enqueue("powershell.exe", Ok(Mixed.Replace("\"0416:00010416\", ", "")));

        await handler.ApplyAsync(Action, captured, ctx, Ct);

        var script = Script(runner.Calls[1]);
        Assert.DoesNotContain("0416:00010416", script);
        Assert.Equal(1, script.Split("'0416:00020409'").Length - 1);
        Assert.Contains("(New-WinUserLanguageList 'en-US')[0]", script);
        Assert.Contains("'0409:00000409'", script);
    }

    [Fact]
    public async Task Apply_adds_ptbr_first_when_the_list_has_none()
    {
        var (handler, runner, ctx) = Setup();
        runner.Enqueue("powershell.exe", Ok("""[ { "LanguageTag": "en-US", "InputMethodTips": [ "0409:00000409" ] } ]"""));
        var captured = await handler.CaptureAsync(Action, ctx, Ct);
        runner.Enqueue("powershell.exe", Ok());
        runner.Enqueue("powershell.exe", Ok(PtBrUsIntl));

        await handler.ApplyAsync(Action, captured, ctx, Ct);

        var script = Script(runner.Calls[1]);
        Assert.True(script.IndexOf("'pt-BR'", StringComparison.Ordinal) < script.IndexOf("'en-US'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_failure_restores_the_previous_list_and_throws()
    {
        var (handler, runner, ctx) = Setup();
        runner.Enqueue("powershell.exe", Ok(Mixed));
        var captured = await handler.CaptureAsync(Action, ctx, Ct);
        runner.Enqueue("powershell.exe", new ProcessResult(1, "", "boom", TimeSpan.Zero, false));
        runner.Enqueue("powershell.exe", Ok());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ApplyAsync(Action, captured, ctx, Ct));

        Assert.Equal(3, runner.Calls.Count);
        var restore = Script(runner.Calls[2]);
        Assert.Contains("'0416:00010416'", restore);
        Assert.Contains("'0416:00020409'", restore);
        Assert.Contains("'0409:00000409'", restore);
    }

    [Fact]
    public async Task Undo_rebuilds_the_journaled_list()
    {
        var (handler, runner, ctx) = Setup();
        runner.Enqueue("powershell.exe", Ok(PtBrDefault));
        var captured = await handler.CaptureAsync(Action, ctx, Ct);
        runner.Enqueue("powershell.exe", Ok());

        await handler.UndoAsync(captured, ctx, Ct);

        var script = Script(runner.Calls[1]);
        Assert.Contains("New-WinUserLanguageList 'pt-BR'", script);
        Assert.Contains("'0416:00010416'", script);
        Assert.Contains("'0416:00000416'", script);
        Assert.Contains("Set-WinUserLanguageList", script);
    }

    [Fact]
    public async Task Undo_failure_throws()
    {
        var (handler, runner, ctx) = Setup();
        runner.Enqueue("powershell.exe", Ok(PtBrDefault));
        var captured = await handler.CaptureAsync(Action, ctx, Ct);
        runner.Enqueue("powershell.exe", new ProcessResult(1, "", "boom", TimeSpan.Zero, false));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.UndoAsync(captured, ctx, Ct));
    }
}
