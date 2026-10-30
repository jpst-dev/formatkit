using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class PowerCfgParseTests
{
    private const string Display = "7516b95f-f776-4464-8c53-06167f40cc99";
    private const string VideoIdle = "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e";
    private static readonly Guid Scheme = BuiltInPowerSchemes.Balanced;
    private static readonly Guid Other = new("11111111-2222-3333-4444-555555555555");
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>Captured from the pt-BR test VM (powercfg /qh).</summary>
    private const string PortugueseOutput =
        "\r\nGUID do Esquema de Energia: 381b4222-f694-41f0-9685-ff5bb260df2e  (Equilibrado)\r\n"
        + "  Alias GUID: SCHEME_BALANCED\r\n"
        + "  GUID de Subgrupos: 7516b95f-f776-4464-8c53-06167f40cc99  (Vídeo)\r\n"
        + "    Alias GUID: SUB_VIDEO\r\n"
        + "    GUID de Configuração de Energia: 3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e  (Desligar o monitor após)\r\n"
        + "      Alias GUID: VIDEOIDLE\r\n"
        + "      Configuração Mínima Possível: 0x00000000\r\n"
        + "      Configuração Máxima Possível: 0xffffffff\r\n"
        + "      Incremento de Configurações Possíveis: 0x00000001\r\n"
        + "      Unidades de Configurações Possíveis: segundos\r\n"
        + "    Índice de Configurações de Correntes Alternadas Atuais: 0x0000012c\r\n"
        + "    Índice de Configurações de Correntes Contínuas Atuais: 0x000000b4\r\n";

    private const string EnglishOutput =
        "\r\nPower Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)\r\n"
        + "  Subgroup GUID: 7516b95f-f776-4464-8c53-06167f40cc99  (Display)\r\n"
        + "    Power Setting GUID: 3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e  (Turn off display after)\r\n"
        + "      Minimum Possible Setting: 0x00000000\r\n"
        + "      Maximum Possible Setting: 0xffffffff\r\n"
        + "      Possible Settings increment: 0x00000001\r\n"
        + "      Possible Settings units: Seconds\r\n"
        + "    Current AC Power Setting Index: 0x00000384\r\n"
        + "    Current DC Power Setting Index: 0x00000258\r\n";

    private static ProcessResult Result(int exit, string stdout = "", string stderr = "", bool timedOut = false) =>
        new(exit, stdout, stderr, TimeSpan.Zero, timedOut);

    private static (PowerCfg Cfg, FakeProcessRunner Runner) Create(params ProcessResult[] results)
    {
        var runner = new FakeProcessRunner();
        foreach (var result in results)
            runner.Enqueue("powercfg.exe", result);
        return (new PowerCfg(runner), runner);
    }

    [Fact]
    public async Task GetValue_parses_english_output()
    {
        var (cfg, runner) = Create(Result(0, EnglishOutput));

        var value = await cfg.GetValueAsync(Scheme, Display, VideoIdle, Ct);

        Assert.Equal((900, 600), value);
        Assert.Equal(["/qh", Scheme.ToString(), Display, VideoIdle], Assert.Single(runner.Calls).Arguments);
    }

    [Fact]
    public async Task GetValue_parses_the_captured_portuguese_output()
    {
        var (cfg, _) = Create(Result(0, PortugueseOutput));

        Assert.Equal((300, 180), await cfg.GetValueAsync(Scheme, Display, VideoIdle, Ct));
    }

    [Fact]
    public async Task GetValue_nonzero_exit_means_the_setting_is_absent()
    {
        var (cfg, _) = Create(Result(1, stderr: "Argumento inválido"));

        Assert.Null(await cfg.GetValueAsync(Scheme, Display, VideoIdle, Ct));
    }

    [Fact]
    public async Task GetValue_without_two_index_lines_throws()
    {
        var (cfg, _) = Create(Result(0, "Current AC Power Setting Index: 0x00000384\r\n"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cfg.GetValueAsync(Scheme, Display, VideoIdle, Ct));
    }

    [Fact]
    public async Task GetValue_timeout_throws()
    {
        var (cfg, _) = Create(Result(-1, timedOut: true));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cfg.GetValueAsync(Scheme, Display, VideoIdle, Ct));
    }

    [Fact]
    public async Task GetValue_keeps_the_all_ones_index_as_minus_one()
    {
        var (cfg, _) = Create(Result(0, "AC: 0xffffffff\r\nDC: 0x00000000\r\n"));

        Assert.Equal((-1, 0), await cfg.GetValueAsync(Scheme, Display, VideoIdle, Ct));
    }

    [Fact]
    public async Task SetValue_on_the_active_scheme_writes_both_indexes_then_activates_it()
    {
        var (cfg, runner) = Create(Result(0), Result(0), Result(0, $"GUID do Esquema de Energia: {Scheme}  (Equilibrado)"), Result(0));

        await cfg.SetValueAsync(Scheme, Display, VideoIdle, 900, 600, Ct);

        Assert.Equal(
            [
                ["/setacvalueindex", Scheme.ToString(), Display, VideoIdle, "900"],
                ["/setdcvalueindex", Scheme.ToString(), Display, VideoIdle, "600"],
                ["/getactivescheme"],
                ["/setactive", Scheme.ToString()],
            ],
            runner.Calls.Select(c => c.Arguments));
    }

    [Fact]
    public async Task SetValue_without_dc_skips_the_dc_command()
    {
        var (cfg, runner) = Create(Result(0), Result(0, $"GUID: {Scheme}"), Result(0));

        await cfg.SetValueAsync(Scheme, Display, VideoIdle, 0, null, Ct);

        Assert.Equal(["/setacvalueindex", "/getactivescheme", "/setactive"], runner.Calls.Select(c => c.Arguments[0]));
    }

    [Fact]
    public async Task SetValue_on_an_inactive_scheme_does_not_activate_it()
    {
        var (cfg, runner) = Create(Result(0), Result(0, $"GUID: {Other}"));

        await cfg.SetValueAsync(Scheme, Display, VideoIdle, 0, null, Ct);

        Assert.Equal(["/setacvalueindex", "/getactivescheme"], runner.Calls.Select(c => c.Arguments[0]));
    }

    [Fact]
    public async Task SetValue_writes_the_all_ones_index_as_unsigned()
    {
        var (cfg, runner) = Create(Result(0), Result(0, $"GUID: {Other}"));

        await cfg.SetValueAsync(Scheme, Display, VideoIdle, -1, null, Ct);

        Assert.Equal("4294967295", runner.Calls[0].Arguments[^1]);
    }

    [Fact]
    public async Task SetValue_failure_throws()
    {
        var (cfg, _) = Create(Result(1, stderr: "erro"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cfg.SetValueAsync(Scheme, Display, VideoIdle, 0, null, Ct));
    }
}
