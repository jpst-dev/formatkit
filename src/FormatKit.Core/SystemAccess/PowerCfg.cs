using System.Text.RegularExpressions;

namespace FormatKit.Core.SystemAccess;

public sealed partial class PowerCfg(IProcessRunner runner) : IPowerSchemes
{
    // Powercfg output is localized, so only GUIDs are ever extracted from it.
    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@":\s*0x([0-9a-fA-F]{1,8})$")]
    private static partial Regex IndexLinePattern();

    public async Task<Guid> GetActiveAsync(CancellationToken ct) =>
        FirstGuid(await RunAsync(["/getactivescheme"], ct).ConfigureAwait(false), "/getactivescheme");

    public async Task<IReadOnlyList<Guid>> ListAsync(CancellationToken ct)
    {
        var output = await RunAsync(["/list"], ct).ConfigureAwait(false);
        return GuidPattern().Matches(output).Select(m => Guid.Parse(m.Value)).ToList();
    }

    public async Task<Guid> DuplicateAsync(Guid source, CancellationToken ct) =>
        FirstGuid(await RunAsync(["/duplicatescheme", source.ToString()], ct).ConfigureAwait(false), "/duplicatescheme");

    public async Task RenameAsync(Guid scheme, string name, string description, CancellationToken ct) =>
        await RunAsync(["/changename", scheme.ToString(), name, description], ct).ConfigureAwait(false);

    public async Task SetActiveAsync(Guid scheme, CancellationToken ct) =>
        await RunAsync(["/setactive", scheme.ToString()], ct).ConfigureAwait(false);

    public async Task DeleteAsync(Guid scheme, CancellationToken ct) =>
        await RunAsync(["/delete", scheme.ToString()], ct).ConfigureAwait(false);

    /// <summary>
    /// Reads with <c>/qh</c> (hidden settings such as PERFEPP only show there). The labels are localized, so the
    /// last two lines ending in <c>: 0x&lt;hex&gt;</c> are taken as AC then DC. A non-zero exit means the setting is absent.
    /// </summary>
    public async Task<(int Ac, int Dc)?> GetValueAsync(Guid scheme, string subgroup, string setting, CancellationToken ct)
    {
        var result = await runner.RunAsync(
            new ProcessSpec("powercfg.exe", ["/qh", scheme.ToString(), subgroup, setting], Timeouts.Query),
            ct).ConfigureAwait(false);

        if (result.TimedOut)
            throw new InvalidOperationException("powercfg /qh timed out.");
        if (result.ExitCode != 0)
            return null;

        var indexes = result.StdOut.Split('\n')
            .Select(line => IndexLinePattern().Match(line.TrimEnd()))
            .Where(m => m.Success)
            .Select(m => unchecked((int)uint.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber)))
            .ToList();
        return indexes.Count >= 2
            ? (indexes[^2], indexes[^1])
            : throw new InvalidOperationException("powercfg /qh printed no AC and DC setting index.");
    }

    public async Task SetValueAsync(Guid scheme, string subgroup, string setting, int ac, int? dc, CancellationToken ct)
    {
        await RunAsync(["/setacvalueindex", scheme.ToString(), subgroup, setting, Index(ac)], ct).ConfigureAwait(false);
        if (dc is { } dcValue)
            await RunAsync(["/setdcvalueindex", scheme.ToString(), subgroup, setting, Index(dcValue)], ct).ConfigureAwait(false);

        // Powercfg only applies a changed index to the running system when the scheme is activated again.
        if (await GetActiveAsync(ct).ConfigureAwait(false) == scheme)
            await SetActiveAsync(scheme, ct).ConfigureAwait(false);
    }

    private static string Index(int value) => unchecked((uint)value).ToString(System.Globalization.CultureInfo.InvariantCulture);

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
