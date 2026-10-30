using CommunityToolkit.Mvvm.ComponentModel;
using FormatKit.App.Localization;
using FormatKit.Core.Steps;

namespace FormatKit.App.ViewModels;

public enum StepState { Pending, Running, Succeeded, Skipped, Failed, RebootRequired }

/// <summary>One step of a running or finished run, with its state, messages and next action.</summary>
public sealed partial class StepRow(string id, string title) : ObservableObject
{
    public string Id { get; } = id;

    public string Title { get; } = title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph), nameof(Color), nameof(StateText), nameof(IsCurrent))]
    private StepState _state = StepState.Pending;

    [ObservableProperty]
    private IReadOnlyList<string> _messages = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextActionText))]
    private string? _nextAction;

    /// <summary>The step opened a console window and waits for the user to type there.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConsoleHint))]
    private bool _isWaitingForConsole;

    public string? ConsoleHint => IsWaitingForConsole ? Strings.Get("RunProfile_TypeInConsole") : null;

    /// <summary>The step that is running now, highlighted on the page.</summary>
    public bool IsCurrent => State == StepState.Running;

    public string Glyph => StepStyle.Glyph(State);

    public string Color => StepStyle.Color(State);

    public string StateText => Strings.Get("StepState_" + State);

    public string? NextActionText =>
        string.IsNullOrWhiteSpace(NextAction) ? null : Strings.Format("Run_NextAction", NextAction);

    /// <summary>Adds a line the step wrote while running; the step's result replaces the messages.</summary>
    public void AddMessage(string text) => Messages = [.. Messages, text];

    public void Finish(StepOutcome outcome, IReadOnlyList<string> messages, string? nextAction)
    {
        State = outcome switch
        {
            StepOutcome.Succeeded => StepState.Succeeded,
            StepOutcome.Skipped => StepState.Skipped,
            StepOutcome.Failed => StepState.Failed,
            _ => StepState.RebootRequired,
        };
        Messages = messages;
        NextAction = nextAction;
        IsWaitingForConsole = false;
    }
}

/// <summary>Icon (Segoe MDL2 Assets glyph) and color of each step state, as <see cref="StatusStyle"/> does for the checklist.</summary>
public static class StepStyle
{
    public static string Glyph(StepState state) => state switch
    {
        StepState.Running => "",
        StepState.Succeeded => "",
        StepState.Skipped => "",
        StepState.Failed => "",
        StepState.RebootRequired => "",
        _ => "",
    };

    public static string Color(StepState state) => state switch
    {
        StepState.Running => "#5B8DB8",
        StepState.Succeeded => "#2E9E5B",
        StepState.Skipped => "#8A8A8A",
        StepState.Failed => "#D13438",
        StepState.RebootRequired => "#E08A00",
        _ => "#8A8A8A",
    };
}
