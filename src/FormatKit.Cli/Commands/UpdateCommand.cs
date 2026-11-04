using System.CommandLine;
using System.Reflection;
using FormatKit.Cli.Localization;
using FormatKit.Core.SystemAccess;
using FormatKit.Hosting;

namespace FormatKit.Cli.Commands;

internal static class UpdateCommand
{
    internal static Func<CommandContext, Updater> CreateUpdater { get; set; } = CreateRealUpdater;

    internal static Updater CreateRealUpdater(CommandContext context)
    {
        var version = typeof(UpdateCommand).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        return new Updater(
            new GitHubReleaseSource(version), new HttpDownloader(), context.Env.Services.Files, context.Env.StateStore,
            Environment.ProcessPath!, version, context.Env.Log);
    }

    public static Command Create(CommandContext context)
    {
        var check = new Option<bool>("--check") { Description = Strings.Get("Opt_UpdateCheck") };
        var yes = new Option<bool>("--yes") { Description = Strings.Get("Opt_UpdateYes") };
        var update = new Command("update", Strings.Get("Cmd_Update")) { Options = { check, yes } };

        update.SetAction(async (parse, ct) =>
        {
            var o = context.Out;
            var updater = CreateUpdater(context);
            var found = await updater.CheckAsync(ct);
            switch (found.Status)
            {
                case UpdateCheckStatus.Failed:
                    o.WriteLine(Strings.Format("Update_CheckFailed", found.Reason ?? ""));
                    return ExitCodes.CompletedWithFailures;
                case UpdateCheckStatus.UpToDate:
                    o.WriteLine(Strings.Format("Update_UpToDate", found.Current));
                    return ExitCodes.Ok;
            }

            o.WriteLine(Strings.Format("Update_Available", found.Current, found.Latest!, found.Release!.Page.AbsoluteUri));
            if (parse.GetValue(check))
                return ExitCodes.Ok;

            if (updater.Refusal() is { } refusal)
            {
                o.WriteLine(refusal);
                return ExitCodes.InvalidInput;
            }

            if (!parse.GetValue(yes))
            {
                if (!context.Env.Services.Console.CanPrompt)
                {
                    o.WriteLine(Strings.Get("Update_NoConsole"));
                    return ExitCodes.CompletedWithFailures;
                }

                o.Write(Strings.Get("Update_Confirm"));
                if (!IsYes(MediaCommands.ReadLine()))
                {
                    o.WriteLine(Strings.Get("Update_Declined"));
                    return ExitCodes.Ok;
                }
            }

            o.WriteLine(Strings.Get("Update_Downloading"));
            var result = await updater.UpdateAsync(found.Release!, ct);
            switch (result.Status)
            {
                case UpdateStatus.Updated:
                    o.WriteLine(Strings.Format("Update_Done", found.Current, found.Latest!));
                    return ExitCodes.Ok;
                case UpdateStatus.Refused:
                    o.WriteLine(result.Reason);
                    return ExitCodes.InvalidInput;
                default:
                    o.WriteLine(Strings.Format("Update_Failed", result.Reason ?? ""));
                    return ExitCodes.CompletedWithFailures;
            }
        });
        return update;
    }

    /// <summary>Accepts the first letter of "yes" and of "sim", whichever language the prompt is in.</summary>
    private static bool IsYes(string? answer) =>
        answer?.Trim() is { Length: > 0 } text && "ys".Contains(char.ToLowerInvariant(text[0]));
}
