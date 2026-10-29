using System.Text.Json;
using FormatKit.Core.Profiles;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Hosting.Tests;

public sealed class PlanServiceTests
{
    private static (Profile Profile, FakeModule Module) Setup()
    {
        var module = new FakeModule("drivers");
        module.Steps.Add(new FakeStep("drivers.scan"));
        module.Steps.Add(new FakeStep("drivers.install"));
        using var doc = JsonDocument.Parse("""{"enabled":true}""");
        var profile = new Profile(1, "p", new Dictionary<string, JsonElement> { ["drivers"] = doc.RootElement.Clone() });
        return (profile, module);
    }

    [Fact]
    public void Build_WithSimulatedStep_WrapsOnlyThatStep()
    {
        var (profile, module) = Setup();

        var plan = PlanService.Build(profile, [module], "DRIVERS.INSTALL");

        Assert.IsNotType<SimulatedRebootStep>(plan[0]);
        Assert.IsType<SimulatedRebootStep>(plan[1]);
        Assert.Equal("drivers.install", plan[1].Id);
    }

    [Fact]
    public void Build_WithoutSimulation_ReturnsPlanUnchanged()
    {
        var (profile, module) = Setup();

        var plan = PlanService.Build(profile, [module], null);

        Assert.Equal(["drivers.scan", "drivers.install"], plan.Select(s => s.Id));
        Assert.All(plan, s => Assert.IsNotType<SimulatedRebootStep>(s));
    }
}
