namespace FormatKit.Cleanup.Diagnosis;

/// <summary>Returns one message per violation; empty means valid.</summary>
public static class KnownIssuesValidator
{
    private static readonly string[] Languages = ["en", "pt-BR"];

    public static IReadOnlyList<string> Validate(KnownIssues issues)
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var issue in issues.Issues)
        {
            if (!seen.Add(issue.Id))
                errors.Add($"{issue.Id}: duplicate id");
            ValidateIssue(issue, errors);
        }
        return errors;
    }

    private static void ValidateIssue(KnownIssue issue, List<string> errors)
    {
        void Error(string message) => errors.Add($"{issue.Id}: {message}");

        if (string.IsNullOrWhiteSpace(issue.Id))
            Error("id is blank");

        foreach (var language in Languages)
        {
            if (issue.Text is null || !issue.Text.TryGetValue(language, out var text) || text is null)
            {
                Error($"missing text for language '{language}'");
                continue;
            }
            CheckField(language, "title", text.Title);
            CheckField(language, "breaks", text.Breaks);
            CheckField(language, "fix", text.Fix);
        }

        void CheckField(string language, string field, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                Error($"text '{language}' field '{field}' is blank");
        }

        switch (issue.Rule)
        {
            case DeviceDisabledRule device:
                if (string.IsNullOrWhiteSpace(device.HardwareId))
                    Error("deviceDisabled needs a hardwareId");
                break;
            case ServiceDisabledRule service:
                if (string.IsNullOrWhiteSpace(service.Service))
                    Error("serviceDisabled needs a service");
                break;
            case RegistryValueRule registry:
                if (string.IsNullOrWhiteSpace(registry.Path) || string.IsNullOrWhiteSpace(registry.Name))
                    Error("registryValue needs a path and a name");
                if ((registry.EqualsValue is null) == (registry.NotEqualsValue is null))
                    Error("registryValue needs exactly one of equals and notEquals");
                if ((registry.Path ?? "").Count(c => c == '*') > 1)
                    Error("registryValue path may contain at most one wildcard");
                break;
            case NonBuiltInPowerSchemeRule:
                break;
            default:
                Error("missing or unknown rule");
                break;
        }
    }
}
