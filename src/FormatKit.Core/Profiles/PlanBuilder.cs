using FormatKit.Core.Steps;

namespace FormatKit.Core.Profiles;

public static class PlanBuilder
{
    public static IReadOnlyList<IStep> Build(Profile profile, IReadOnlyList<IModule> modules)
    {
        var steps = new List<IStep>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var module in modules)
        {
            if (!profile.Modules.TryGetValue(module.Name, out var section) || !module.IsEnabled(section))
                continue;

            foreach (var step in module.CreateSteps(section, profile.BaseDirectory))
            {
                if (!seen.Add(step.Id))
                    throw new InvalidOperationException($"Duplicate step id '{step.Id}' (module '{module.Name}').");
                steps.Add(step);
            }
        }

        return steps;
    }
}
