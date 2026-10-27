using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class PowerCfgTests
{
    private static readonly Guid Other = new("11111111-2222-3333-4444-555555555555");

    private static ProcessResult Result(int exit, string stdout = "", string stderr = "") =>
        new(exit, stdout, stderr, TimeSpan.Zero, false);

    private static (PowerCfg Cfg, FakeProcessRunner Runner) Create(ProcessResult result)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("powercfg.exe", result);
        return (new PowerCfg(runner), runner);
    }

    [Fact]
    public async Task GetActive_parses_guid_from_portuguese_output()
    {
        var (cfg, runner) = Create(Result(0, "GUID do Esquema de Energia: 381b4222-f694-41f0-9685-ff5bb260df2e  (Equilibrado)"));

        var active = await cfg.GetActiveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BuiltInPowerSchemes.Balanced, active);
        Assert.Equal(["/getactivescheme"], Assert.Single(runner.Calls).Arguments);
    }

    [Fact]
    public async Task GetActive_parses_all_zero_guid()
    {
        var (cfg, _) = Create(Result(0, "GUID do Esquema de Energia: 00000000-0000-0000-0000-000000000000  (TweaksBW Ultimate)"));

        Assert.Equal(Guid.Empty, await cfg.GetActiveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task List_returns_every_guid()
    {
        var (cfg, runner) = Create(Result(0,
            "Esquemas de Energia existentes (* Ativo)\r\n-----------------------------------\r\n"
            + "GUID do Esquema de Energia: 381b4222-f694-41f0-9685-ff5bb260df2e  (Equilibrado) *\r\n"
            + "GUID do Esquema de Energia: 11111111-2222-3333-4444-555555555555  (Meu plano)\r\n"));

        var schemes = await cfg.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([BuiltInPowerSchemes.Balanced, Other], schemes);
        Assert.Equal(["/list"], Assert.Single(runner.Calls).Arguments);
    }

    [Fact]
    public async Task Duplicate_returns_new_guid()
    {
        var (cfg, runner) = Create(Result(0, "GUID do Esquema de Energia: 11111111-2222-3333-4444-555555555555  (Equilibrado)"));

        var created = await cfg.DuplicateAsync(BuiltInPowerSchemes.Balanced, TestContext.Current.CancellationToken);

        Assert.Equal(Other, created);
        Assert.Equal(["/duplicatescheme", BuiltInPowerSchemes.Balanced.ToString()], Assert.Single(runner.Calls).Arguments);
    }

    [Fact]
    public async Task SetActive_and_Delete_pass_the_guid()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue("powercfg.exe", Result(0));
        runner.Enqueue("powercfg.exe", Result(0));
        var cfg = new PowerCfg(runner);

        await cfg.SetActiveAsync(Other, TestContext.Current.CancellationToken);
        await cfg.DeleteAsync(Other, TestContext.Current.CancellationToken);

        Assert.Equal(["/setactive", Other.ToString()], runner.Calls[0].Arguments);
        Assert.Equal(["/delete", Other.ToString()], runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task Nonzero_exit_throws()
    {
        var (cfg, _) = Create(Result(1, stderr: "erro"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cfg.GetActiveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Missing_guid_throws()
    {
        var (cfg, _) = Create(Result(0, "nothing here"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cfg.GetActiveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void IsBuiltIn_recognizes_the_three_schemes()
    {
        Assert.True(BuiltInPowerSchemes.IsBuiltIn(BuiltInPowerSchemes.Balanced));
        Assert.True(BuiltInPowerSchemes.IsBuiltIn(BuiltInPowerSchemes.HighPerformance));
        Assert.True(BuiltInPowerSchemes.IsBuiltIn(BuiltInPowerSchemes.Ultimate));
        Assert.False(BuiltInPowerSchemes.IsBuiltIn(Other));
    }
}
