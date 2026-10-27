using FormatKit.Core.Steps;

namespace FormatKit.Core.Running;

public sealed record RunRequest(
    string ProfilePath,
    string ProfileHash,
    IReadOnlyList<IStep> Plan,
    StepContext Context,
    bool Resume);
