using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;

namespace FormatKit.Hosting;

public static class PlanService
{
    /// <summary>Builds the plan; <paramref name="simulateRebootAfterStep"/> wraps that step in a <see cref="SimulatedRebootStep"/> (VM tests).</summary>
    public static IReadOnlyList<IStep> Build(Profile profile, IReadOnlyList<IModule> modules, string? simulateRebootAfterStep) =>
        SimulatedRebootStep.Apply(PlanBuilder.Build(profile, modules), simulateRebootAfterStep);
}
