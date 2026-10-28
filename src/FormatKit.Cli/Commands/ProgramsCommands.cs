using System.CommandLine;
using System.Text.Json;
using FormatKit.Cli.Localization;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Programs.Catalog;
using FormatKit.Programs.Steps;

namespace FormatKit.Cli.Commands;

internal static class ProgramsCommands
{
    public static Command Create(CommandContext context)
    {
        var programs = new Command("programs", Strings.Get("Cmd_Programs"));
        programs.Subcommands.Add(CreateList(context));
        programs.Subcommands.Add(CreateSearch(context));
        programs.Subcommands.Add(CreateExport(context));
        return programs;
    }

    /// <summary>The ` [user]` suffix shown after a program installed in the signed-in user's context.</summary>
    internal static string UserMarker(InstallContext installContext) =>
        installContext == InstallContext.User ? $" [{Strings.Get("Marker_User")}]" : "";

    /// <summary>The indented dry-run lines that list the programs of a programs step.</summary>
    internal static IEnumerable<string> DryRunLines(IStep step)
    {
        switch (step)
        {
            case WingetProgramsStep winget:
                foreach (var p in winget.Programs)
                    yield return $"    {p.Key}  {p.DisplayName}{UserMarker(p.Package.Context)}";
                break;
            case DirectProgramsStep direct:
                foreach (var e in direct.Entries)
                    yield return $"    {e.Name} ({e.Url ?? e.Path})";
                break;
        }
    }

    private static string CategoryName(ProgramCategory category)
    {
        var name = category.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static Command CreateList(CommandContext context)
    {
        var category = new Option<string?>("--category") { Description = Strings.Get("Opt_ProgramsCategory") };
        var list = new Command("list", Strings.Get("Cmd_ProgramsList")) { Options = { category } };

        list.SetAction(parse =>
        {
            var o = context.Out;
            ProgramCategory? only = null;
            if (parse.GetValue(category) is { } requested)
            {
                var match = Enum.GetValues<ProgramCategory>().Cast<ProgramCategory?>()
                    .FirstOrDefault(c => string.Equals(CategoryName(c!.Value), requested, StringComparison.Ordinal));
                if (match is null)
                {
                    var valid = string.Join(", ", Enum.GetValues<ProgramCategory>().Select(CategoryName));
                    o.WriteLine(Strings.Format("Programs_InvalidCategory", requested, valid));
                    return ExitCodes.InvalidInput;
                }

                only = match;
            }

            var verbose = parse.GetValue<bool>("--verbose");
            var items = context.Programs.Programs.Where(p => only is null || p.Category == only).ToList();
            var idWidth = items.Count == 0 ? 0 : items.Max(p => p.Id.Length);
            var catWidth = items.Count == 0 ? 0 : items.Max(p => CategoryName(p.Category).Length);
            foreach (var item in items)
            {
                var text = item.LocalizedText();
                o.WriteLine(
                    $"{item.Id.PadRight(idWidth)}  {CategoryName(item.Category).PadRight(catWidth)}  {text.Name}{UserMarker(item.Context)}");
                if (!verbose)
                    continue;

                o.WriteLine("    " + text.Description);
                o.WriteLine("    " + Strings.Format("Programs_Winget", item.WingetId, item.Source));
            }

            return ExitCodes.Ok;
        });
        return list;
    }

    private static Command CreateSearch(CommandContext context)
    {
        var term = new Argument<string>("term") { Description = Strings.Get("Arg_ProgramsTerm") };
        var search = new Command("search", Strings.Get("Cmd_ProgramsSearch")) { Arguments = { term } };

        search.SetAction(async (parse, ct) =>
        {
            var o = context.Out;
            var value = parse.GetValue(term)!;
            if (string.IsNullOrWhiteSpace(value))
            {
                o.WriteLine(Strings.Get("Programs_SearchEmpty"));
                return ExitCodes.InvalidInput;
            }

            ProcessResult result;
            try
            {
                result = await context.Env.Services.Winget.SearchAsync(value, ct);
            }
            catch (FileNotFoundException)
            {
                o.WriteLine(Strings.Get("Programs_WingetMissing"));
                return ExitCodes.CompletedWithFailures;
            }

            // winget's own text is passed through untouched; it is localized by winget and never parsed.
            if (result.StdOut.Length > 0)
                o.Write(result.StdOut);
            if (result.StdErr.Length > 0)
                o.Write(result.StdErr);
            return !result.TimedOut && result.ExitCode == 0 ? ExitCodes.Ok : ExitCodes.CompletedWithFailures;
        });
        return search;
    }

    private static Command CreateExport(CommandContext context)
    {
        var file = new Argument<string>("file") { Description = Strings.Get("Arg_ProgramsFile") };
        var export = new Command("export", Strings.Get("Cmd_ProgramsExport")) { Arguments = { file } };

        export.SetAction(async (parse, ct) =>
        {
            var o = context.Out;
            string full;
            try
            {
                full = Path.GetFullPath(parse.GetValue(file)!);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                o.WriteLine(Strings.Format("Programs_ExportBadPath", parse.GetValue(file)!));
                return ExitCodes.InvalidInput;
            }

            var result = await context.Env.Services.Winget.ExportAsync(full, ct);
            switch (result.Status)
            {
                case WingetStatus.WingetMissing:
                    o.WriteLine(Strings.Get("Programs_WingetMissing"));
                    return ExitCodes.CompletedWithFailures;
                case WingetStatus.Failed:
                    o.WriteLine(Strings.Format("Programs_ExportFailed", result.ExitCode));
                    if (!string.IsNullOrWhiteSpace(result.Output))
                        o.WriteLine(result.Output.TrimEnd());
                    return ExitCodes.CompletedWithFailures;
            }

            o.WriteLine(Strings.Format("Programs_ExportDone", full));
            o.WriteLine(Strings.Get("Programs_ExportUsage"));
            o.WriteLine("  \"programs\": { \"enabled\": true, \"acceptAgreements\": true, \"importFile\": "
                + JsonSerializer.Serialize(full) + " }");
            return ExitCodes.Ok;
        });
        return export;
    }
}
