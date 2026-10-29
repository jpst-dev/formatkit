using System.CommandLine;
using System.Text.Json;
using System.Text.RegularExpressions;
using FormatKit.Cli.Localization;
using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;
using FormatKit.Dev;
using FormatKit.Dev.Steps;
using DevStrings = FormatKit.Dev.Localization.Strings;

namespace FormatKit.Cli.Commands;

internal static partial class DevCommands
{
    [GeneratedRegex(@"\A[a-z_][a-z0-9_-]{0,31}\z")]
    private static partial Regex UserPattern();

    public static Command Create(CommandContext context)
    {
        var dev = new Command("dev", Strings.Get("Cmd_Dev"));
        dev.Subcommands.Add(CreateWslUser(context));
        dev.Subcommands.Add(CreateSshKey(context));
        return dev;
    }

    private static Command CreateWslUser(CommandContext context)
    {
        var profile = new Option<string?>("--profile") { Description = Strings.Get("Opt_Profile") };
        var command = new Command("wsl-user", Strings.Get("Cmd_DevWslUser")) { Options = { profile } };
        command.SetAction(async (parse, ct) =>
        {
            if (!RequireConsole(context))
                return ExitCodes.InvalidInput;
            if (!TryReadOptions(context, parse.GetValue(profile), out var options))
                return ExitCodes.InvalidInput;

            var wsl = options.Wsl ?? DefaultWsl(context);
            if (wsl is null)
                return ExitCodes.InvalidInput;

            var item = await WslStep.EnsureUserAsync(wsl, new StepContext(context.Env.Services, context.Env.Log), ct);
            return Report(context, [item]);
        });
        return command;
    }

    private static Command CreateSshKey(CommandContext context)
    {
        var profile = new Option<string?>("--profile") { Description = Strings.Get("Opt_Profile") };
        var command = new Command("ssh-key", Strings.Get("Cmd_DevSshKey")) { Options = { profile } };
        command.SetAction(async (parse, ct) =>
        {
            if (!RequireConsole(context))
                return ExitCodes.InvalidInput;
            if (!TryReadOptions(context, parse.GetValue(profile), out var options))
                return ExitCodes.InvalidInput;

            var env = DevEnvironmentOf(context);
            var ssh = options.Ssh ?? new SshOptions("ed25519", DefaultComment(env));

            // Setting ssh-agent to Automatic needs administrator rights.
            if (await context.EnsureElevatedAsync() is { } exit)
                return exit;

            var items = await SshStep.RunAsync(ssh, env, new StepContext(context.Env.Services, context.Env.Log), ct);
            return Report(context, items);
        });
        return command;
    }

    private static DevEnvironment DevEnvironmentOf(CommandContext context) => context.Env.Dev ?? DevEnvironment.Current();

    private static bool RequireConsole(CommandContext context)
    {
        if (context.Env.Services.Console.CanPrompt)
            return true;

        context.Out.WriteLine(Strings.Get("Dev_Redirected"));
        return false;
    }

    /// <summary>The profile's parsed dev section; all-null options when there is no profile or no dev section.</summary>
    private static bool TryReadOptions(CommandContext context, string? profilePath, out DevOptions options)
    {
        options = new DevOptions(null, null, null, null, null, null);
        if (profilePath is null)
            return true;

        var result = new ProfileLoader(context.Modules).Load(profilePath);
        if (!result.IsValid)
        {
            context.Out.WriteLine(Strings.Format("Profile_Errors", result.Errors.Count));
            foreach (var error in result.Errors)
                context.Out.WriteLine(error.Path.Length == 0 ? $"  {error.Message}" : $"  {error.Path}: {error.Message}");
            return false;
        }

        var profile = result.Value!.Profile;
        if (!profile.Modules.TryGetValue("dev", out JsonElement section))
            return true;

        var (parsed, errors) = DevOptions.Parse(section, "modules.dev", profile.BaseDirectory, DevEnvironmentOf(context));
        if (parsed is null)
        {
            context.Out.WriteLine(Strings.Format("Dev_ProfileSection", errors.Count));
            foreach (var error in errors)
                context.Out.WriteLine($"  {error.Path}: {error.Message}");
            return false;
        }

        options = parsed;
        return true;
    }

    /// <summary>The Ubuntu default with the lowercased Windows user name; null (after a message) when that is no Linux user name.</summary>
    private static WslOptions? DefaultWsl(CommandContext context)
    {
        var name = DevEnvironmentOf(context).UserName.ToLowerInvariant();
        if (name == "root" || !UserPattern().IsMatch(name))
        {
            context.Out.WriteLine(Strings.Format("Dev_UserNameUnusable", name));
            return null;
        }

        return new WslOptions("Ubuntu", name, null);
    }

    /// <summary><c>user@computer</c> in the characters the SSH comment allows, as the profile default.</summary>
    private static string DefaultComment(DevEnvironment env)
    {
        var raw = $"{env.UserName}@{env.ComputerName}".ToLowerInvariant();
        var safe = string.Concat(raw.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '@' or '+' or '-' ? c : '-'));
        return safe.Length > 100 ? safe[..100] : safe;
    }

    private static int Report(CommandContext context, IReadOnlyList<DevItem> items)
    {
        foreach (var item in items)
        {
            var line = $"{item.Label}: {DevStrings.Get($"Outcome_{item.Outcome}")}";
            context.Out.WriteLine(string.IsNullOrWhiteSpace(item.Detail) ? line : $"{line} — {item.Detail}");
            if (item.Outcome is DevItemOutcome.Pending or DevItemOutcome.Failed && !string.IsNullOrWhiteSpace(item.NextAction))
                context.Out.WriteLine("  " + Strings.Format("Dev_NextAction", item.NextAction!));
        }

        return items.Any(i => i.Outcome is DevItemOutcome.Pending or DevItemOutcome.Failed)
            ? ExitCodes.CompletedWithFailures
            : ExitCodes.Ok;
    }
}
