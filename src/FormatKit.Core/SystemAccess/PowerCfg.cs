using System.Text.RegularExpressions;

namespace FormatKit.Core.SystemAccess;

public sealed partial class PowerCfg(IProcessRunner runner) : IPowerSchemes
{
    // Powercfg output is localized, so only GUIDs are ever extracted from it.
    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    public async Task<Guid> GetActiveAsync(CancellationToken ct) =>
        FirstGuid(await RunAsync(["/getactivescheme"], ct).ConfigureAwait(false), "/getactivescheme");

    public async Task<IReadOnlyList<Guid>> ListAsync(CancellationToken ct)
    {
        var output = await RunAsync(["/list"], ct).ConfigureAwait(false);
        return GuidPattern().Matches(output).Select(m => Guid.Parse(m.Value)).ToList();
    }

    public async Task<Guid> DuplicateAsync(Guid source, CancellationToken ct) =>
        FirstGuid(await RunAsync(["/duplicatescheme", source.ToString()], ct).ConfigureAwait(false), "/duplicatescheme");

    public async Task SetActiveAsync(Guid scheme, CancellationToken ct) =>
        await RunAsync(["/setactive", scheme.ToString()], ct).ConfigureAwait(false);

    public async Task DeleteAsync(Guid scheme, CancellationToken ct) =>
        await RunAsync(["/delete", scheme.ToString()], ct).ConfigureAwait(false);

    private async Task<string> RunAsync(string[] arguments, CancellationToken ct)
    {
        var result = await runner.RunAsync(
            new ProcessSpec("powercfg.exe", arguments, Timeouts.Query),
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException(
                $"powercfg {arguments[0]} failed (exit {result.ExitCode}): {result.StdErr.Trim()}");
        }

        return result.StdOut;
    }

    private static Guid FirstGuid(string output, string command)
    {
        var match = GuidPattern().Match(output);
        return match.Success
            ? Guid.Parse(match.Value)
            : throw new InvalidOperationException($"powercfg {command} printed no scheme GUID.");
    }
}
