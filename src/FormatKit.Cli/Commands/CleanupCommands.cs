using System.CommandLine;
using FormatKit.Cleanup;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Diagnosis;
using FormatKit.Cleanup.Journal;
using FormatKit.Cli.Localization;
using FormatKit.Core;
using FormatKit.Core.Steps;

namespace FormatKit.Cli.Commands;

internal static class CleanupCommands
{
    private static readonly string[] Levels = ["safe", "balanced", "aggressive"];
    private static readonly string[] Categories = ["apps", "privacy", "performance"];

    public static Command Create(CommandContext context)
    {
        var cleanup = new Command("cleanup", Strings.Get("Cmd_Cleanup"));
        cleanup.Subcommands.Add(CreateList(context));
        cleanup.Subcommands.Add(CreateCheck(context));
        cleanup.Subcommands.Add(CreateUndo(context));
        return cleanup;
    }

    /// <summary>The ` [level]` and ` [contested]` suffix shown after an item's title in the dry-run plan.</summary>
    internal static string Markers(CleanupItem item) =>
        $" [{LevelName(item.Level)}]" + (item.Contested ? $" [{Strings.Get("Marker_Contested")}]" : "");

    private static string LevelName(CleanupLevel level) => level.ToString().ToLowerInvariant();

    private static Command CreateList(CommandContext context)
    {
        var level = new Option<string?>("--level") { Description = Strings.Get("Opt_CleanupLevel") };
        level.AcceptOnlyFromAmong(Levels);
        var category = new Option<string?>("--category") { Description = Strings.Get("Opt_CleanupCategory") };
        category.AcceptOnlyFromAmong(Categories);
        var list = new Command("list", Strings.Get("Cmd_CleanupList")) { Options = { level, category } };

        list.SetAction(parse =>
        {
            var maxLevel = parse.GetValue(level) is { } l ? Enum.Parse<CleanupLevel>(l, ignoreCase: true) : (CleanupLevel?)null;
            var only = parse.GetValue(category) is { } c ? Enum.Parse<CleanupCategory>(c, ignoreCase: true) : (CleanupCategory?)null;
            var verbose = parse.GetValue<bool>("--verbose");

            var items = context.Cleanup.Items
                .Where(i => maxLevel is null || i.Level <= maxLevel)
                .Where(i => only is null || i.Category == only)
                .ToList();

            var idWidth = items.Count == 0 ? 0 : items.Max(i => i.Id.Length);
            var contested = $"[{Strings.Get("Marker_Contested")}]";
            foreach (var item in items)
            {
                var text = item.LocalizedText();
                context.Out.WriteLine(
                    $"{item.Id.PadRight(idWidth)}  {LevelName(item.Level).PadRight(10)}  " +
                    $"{(item.Contested ? contested : "").PadRight(contested.Length)}  {text.Title}");
                if (!verbose)
                    continue;

                context.Out.WriteLine("    " + Strings.Format("List_What", text.What));
                context.Out.WriteLine("    " + Strings.Format("List_Breaks", text.Breaks));
                context.Out.WriteLine("    " + Strings.Format("List_Gain", text.Gain));
            }

            return ExitCodes.Ok;
        });
        return list;
    }

    private static Command CreateCheck(CommandContext context)
    {
        var check = new Command("check", Strings.Get("Cmd_CleanupCheck"));
        check.SetAction(async (_, ct) => await Output.GuardSystemQueryAsync(context.Out, async () =>
        {
            var report = await new CleanupDiagnosis(context.Issues, AppPaths.Default, context.Env.Log)
                .RunAsync(context.Env.Services, ct);
            PrintCheck(context.Out, report);
            return report.Findings.Any(f => f.Issue.Severity is IssueSeverity.Breaks or IssueSeverity.Security)
                ? ExitCodes.CompletedWithFailures
                : ExitCodes.Ok;
        }));
        return check;
    }

    private static void PrintCheck(TextWriter o, DiagnosisReport report)
    {
        if (report.Findings.Count == 0)
            o.WriteLine(Strings.Get("Check_NoFindings"));

        foreach (var group in report.Findings.GroupBy(f => f.Issue.Severity))
        {
            o.WriteLine(Strings.Get("Check_Group_" + group.Key));
            foreach (var finding in group)
            {
                var text = finding.Issue.LocalizedText();
                o.WriteLine("  " + Strings.Format("Check_Finding", text.Title, finding.Detail));
                o.WriteLine("    " + Strings.Format("Check_Breaks", text.Breaks));
                o.WriteLine("    " + Strings.Format("Check_Fix", text.Fix));
            }
        }

        if (report.Applied.Count > 0)
        {
            o.WriteLine(Strings.Get("Check_Applied"));
            foreach (var applied in report.Applied)
                o.WriteLine("  " + applied.ItemId + (applied.CanUndo ? "" : " " + Strings.Get("Check_UndoOtherUser")));
        }

        if (report.JournalError is not null)
            o.WriteLine(Strings.Format("Check_JournalError", report.JournalError));

        if (report.NotChecked > 0)
            o.WriteLine(Strings.Format("Check_NotChecked", report.NotChecked));
    }

    private static Command CreateUndo(CommandContext context)
    {
        var ids = new Argument<string[]>("ids") { Description = Strings.Get("Arg_UndoIds"), Arity = ArgumentArity.ZeroOrMore };
        var all = new Option<bool>("--all") { Description = Strings.Get("Opt_UndoAll") };
        var undo = new Command("undo", Strings.Get("Cmd_CleanupUndo")) { Arguments = { ids }, Options = { all } };

        undo.SetAction(async (parse, ct) =>
        {
            var o = context.Out;
            var requested = parse.GetValue(ids) ?? [];
            var everything = parse.GetValue(all);

            if (everything && requested.Length > 0)
            {
                o.WriteLine(Strings.Get("Undo_UsageBoth"));
                return ExitCodes.InvalidInput;
            }

            if (!everything && requested.Length == 0)
            {
                o.WriteLine(Strings.Get("Undo_UsageNeither"));
                return ExitCodes.InvalidInput;
            }

            // Ids dropped from the catalog stay undoable while the journal still records them.
            var loaded = new JournalStore(context.Env.Services.Files, AppPaths.Default).Load();
            var journaled = loaded.Journal?.Entries.Select(e => e.ItemId).ToHashSet(StringComparer.Ordinal) ?? [];
            var unknown = requested.Where(id => context.Cleanup.Find(id) is null && !journaled.Contains(id)).ToList();
            if (unknown.Count > 0 && loaded.Status != JournalLoadStatus.Corrupt)
            {
                o.WriteLine(Strings.Format("Undo_UnknownIds", string.Join(", ", unknown)));
                return ExitCodes.InvalidInput;
            }

            if (await context.EnsureElevatedAsync() is { } exit)
                return exit;

            IReadOnlyList<ItemOutcome> outcomes;
            try
            {
                var undoService = new CleanupUndo(context.Cleanup, AppPaths.Default, context.Env.Services.Files);
                outcomes = await undoService.UndoAsync(
                    everything ? null : requested, new StepContext(context.Env.Services, context.Env.Log), ct);
            }
            catch (JournalCorruptException ex)
            {
                o.WriteLine(Strings.Format("Undo_JournalCorrupt", ex.Path, ex.Error));
                return ExitCodes.CompletedWithFailures;
            }

            if (outcomes.Count == 0)
                o.WriteLine(Strings.Get("Undo_NothingToDo"));

            foreach (var outcome in outcomes)
            {
                o.WriteLine(outcome.Status switch
                {
                    ItemStatus.Applied => Strings.Format("Undo_Line_Reverted", outcome.ItemId),
                    ItemStatus.Failed => Strings.Format("Undo_Line_Failed", outcome.ItemId, outcome.Message),
                    _ => Strings.Format("Undo_Line_Skipped", outcome.ItemId, outcome.Message),
                });
            }

            return outcomes.Any(x => x.Status == ItemStatus.Failed) ? ExitCodes.CompletedWithFailures : ExitCodes.Ok;
        });
        return undo;
    }
}
