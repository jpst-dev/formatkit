using System.Text;
using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Actions;

/// <summary>Makes pt-BR the first input language with only the US-International keyboard; other languages keep their order and inputs.</summary>
internal sealed class KeyboardLayoutHandler : IActionHandler
{
    public const string Name = "keyboardLayout";

    private const string PtBr = "pt-BR";
    private const string UsInternational = "0416:00020409";

    // ConvertTo-Json -InputObject keeps a one-language list an array; piping it would unwrap it in Windows PowerShell 5.1.
    private const string ReadScript =
        "ConvertTo-Json -InputObject @(Get-WinUserLanguageList | ForEach-Object { [pscustomobject]@{ LanguageTag = $_.LanguageTag; InputMethodTips = @($_.InputMethodTips) } }) -Depth 3 -Compress";

    private sealed record Language(string Tag, IReadOnlyList<string> Tips);

    public async Task<bool> IsAppliedAsync(CleanupAction action, StepContext ctx, CancellationToken ct) =>
        IsDesired(await ReadAsync(ctx, ct).ConfigureAwait(false));

    public async Task<JournalAction> CaptureAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var languages = await ReadAsync(ctx, ct).ConfigureAwait(false);
        ctx.Log.Debug("Keyboard layout previous list: {Languages}", string.Join("; ", languages.Select(l => $"{l.Tag}={string.Join(",", l.Tips)}")));
        return new JournalAction(Name, Name, JsonSerializer.SerializeToElement(new
        {
            languages = languages.Select(l => new { tag = l.Tag, tips = l.Tips }).ToList(),
        }));
    }

    public async Task<JournalAction> ApplyAsync(CleanupAction action, JournalAction captured, StepContext ctx, CancellationToken ct)
    {
        var previous = FromJournal(captured);
        var desired = new List<Language> { new(PtBr, [UsInternational]) };
        desired.AddRange(previous.Where(l => !string.Equals(l.Tag, PtBr, StringComparison.OrdinalIgnoreCase)));

        try
        {
            await RunAsync(ctx, SetScript(desired), "Set-WinUserLanguageList", ct).ConfigureAwait(false);
            if (!IsDesired(await ReadAsync(ctx, ct).ConfigureAwait(false)))
            {
                throw new InvalidOperationException("The keyboard layout did not take effect.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ctx.Log.Warning(ex, "Keyboard layout change failed; restoring the previous language list");
            await TryRestoreAsync(previous, ctx).ConfigureAwait(false);
            throw;
        }

        ctx.Log.Information("Keyboard layout set to pt-BR US-International");
        return captured;
    }

    public async Task UndoAsync(JournalAction recorded, StepContext ctx, CancellationToken ct)
    {
        var previous = FromJournal(recorded);
        if (previous.Count == 0)
        {
            ctx.Log.Warning("The journaled language list was empty; the keyboard layout was not restored");
            return;
        }

        await RunAsync(ctx, SetScript(previous), "Set-WinUserLanguageList", ct).ConfigureAwait(false);
        ctx.Log.Information("Keyboard layout restored");
    }

    private static async Task TryRestoreAsync(IReadOnlyList<Language> previous, StepContext ctx)
    {
        if (previous.Count == 0)
        {
            return;
        }

        try
        {
            await RunAsync(ctx, SetScript(previous), "Set-WinUserLanguageList", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception restoreError)
        {
            ctx.Log.Warning(restoreError, "Could not restore the previous language list");
        }
    }

    private static bool IsDesired(IReadOnlyList<Language> languages) =>
        languages.Count > 0
        && string.Equals(languages[0].Tag, PtBr, StringComparison.OrdinalIgnoreCase)
        && languages[0].Tips.Count == 1
        && string.Equals(languages[0].Tips[0], UsInternational, StringComparison.OrdinalIgnoreCase);

    private static string SetScript(IReadOnlyList<Language> languages)
    {
        var sb = new StringBuilder("$ErrorActionPreference='Stop'; ");
        for (var i = 0; i < languages.Count; i++)
        {
            var language = languages[i];
            var tag = PowerShellText.SingleQuote(language.Tag);
            sb.Append(i == 0 ? $"$l=New-WinUserLanguageList {tag}; $x=$l[0]; " : $"$x=(New-WinUserLanguageList {tag})[0]; ");
            sb.Append("$x.InputMethodTips.Clear(); ");
            foreach (var tip in language.Tips)
            {
                sb.Append($"$x.InputMethodTips.Add({PowerShellText.SingleQuote(tip)}); ");
            }

            if (i > 0)
            {
                sb.Append("$l.Add($x); ");
            }
        }

        return sb.Append("Set-WinUserLanguageList $l -Force").ToString();
    }

    private static async Task<IReadOnlyList<Language>> ReadAsync(StepContext ctx, CancellationToken ct)
    {
        var output = await RunAsync(ctx, ReadScript, "Get-WinUserLanguageList", ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(output.Trim().TrimStart('﻿'));
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
        return items.Select(item => new Language(
            item.GetProperty("LanguageTag").GetString()!,
            Tips(item.TryGetProperty("InputMethodTips", out var tips) ? tips : default))).ToList();
    }

    private static List<string> Tips(JsonElement tips) => tips.ValueKind switch
    {
        JsonValueKind.Array => tips.EnumerateArray().Select(t => t.GetString()!).ToList(),
        JsonValueKind.String => [tips.GetString()!],
        _ => [],
    };

    private static List<Language> FromJournal(JournalAction recorded) =>
        recorded.Previous.GetProperty("languages").EnumerateArray()
            .Select(l => new Language(l.GetProperty("tag").GetString()!, Tips(l.GetProperty("tips"))))
            .ToList();

    private static async Task<string> RunAsync(StepContext ctx, string script, string operation, CancellationToken ct)
    {
        var result = await ctx.Services.Processes.RunAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], Timeouts.Query), ct).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException($"{operation} failed (exit {result.ExitCode}): {result.StdErr.Trim()}");
        }

        return result.StdOut;
    }
}
