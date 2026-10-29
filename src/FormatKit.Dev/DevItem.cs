using FormatKit.Core.Steps;
using FormatKit.Dev.Localization;

namespace FormatKit.Dev;

public enum DevItemOutcome { Done, AlreadyInOrder, Pending, NotApplicable, Failed }

/// <param name="NextAction">What the user does about a Pending or Failed item.</param>
/// <param name="Detail">Extra text appended to the item's message.</param>
public sealed record DevItem(string Label, DevItemOutcome Outcome, string? NextAction = null, string? Detail = null);

internal static class DevStepResult
{
    /// <summary>One message per item; any Pending or Failed item fails the step, otherwise a reboot is reported if asked for.</summary>
    public static StepResult From(IReadOnlyList<DevItem> items, bool reboot = false)
    {
        var messages = items.Select(Message).ToArray();

        if (items.Any(i => i.Outcome is DevItemOutcome.Pending or DevItemOutcome.Failed))
        {
            var actions = items
                .Where(i => i.Outcome is DevItemOutcome.Pending or DevItemOutcome.Failed && !string.IsNullOrWhiteSpace(i.NextAction))
                .Select(i => i.NextAction!)
                .Distinct(StringComparer.Ordinal);
            var next = string.Join(" ", actions);
            return StepResult.Fail(next.Length == 0 ? null : next, messages);
        }

        return reboot ? StepResult.Reboot(messages) : StepResult.Success(messages);
    }

    private static string Message(DevItem item)
    {
        var text = $"{item.Label}: {Strings.Get($"Outcome_{item.Outcome}")}";
        return string.IsNullOrWhiteSpace(item.Detail) ? text : $"{text} — {item.Detail}";
    }
}
