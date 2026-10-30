using System.CommandLine;
using FormatKit.Cli.Commands;
using FormatKit.Cli.Localization;
using FormatKit.Cleanup;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Diagnosis;
using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Drivers;
using FormatKit.Drivers.Catalog;
using FormatKit.Hosting;
using FormatKit.Programs.Catalog;

namespace FormatKit.Cli;

public sealed class CliApp
{
    internal const string ElevatedChildFlag = "--elevated-child";
    internal const string WaitOnExitFlag = "--wait-on-exit";

    private readonly CliEnvironment _env;
    private readonly IReadOnlyList<IModule> _modules;
    private readonly VendorCatalog _catalog;
    private readonly CleanupCatalog _cleanup;
    private readonly KnownIssues _issues;
    private readonly ProgramCatalog _programs;

    public CliApp(CliEnvironment env)
        : this(env, ModuleSet.CreateReal(env.Services, env.Log, env.NetworkRetryDelay, env.Dev))
    {
    }

    private CliApp(CliEnvironment env, ModuleSet set)
        : this(env, set.Vendors, set.Modules, set.Cleanup, set.Issues, set.Programs)
    {
    }

    /// <summary>Test seam: lets tests plan with fake modules.</summary>
    internal CliApp(
        CliEnvironment env, VendorCatalog catalog, IReadOnlyList<IModule> modules,
        CleanupCatalog? cleanup = null, KnownIssues? issues = null, ProgramCatalog? programs = null)
    {
        _env = env;
        _catalog = catalog;
        _cleanup = cleanup ?? CleanupCatalog.LoadEmbedded();
        _issues = issues ?? KnownIssues.LoadEmbedded();
        _programs = programs ?? ProgramCatalog.LoadEmbedded();
        _modules = modules;
    }

    /// <summary>The modules <see cref="RunAsync"/> plans with; exposed so tests can check their order.</summary>
    internal IReadOnlyList<IModule> Modules => _modules;

    /// <summary>Test seam: waits for the user to close an elevated child window. Null uses the console.</summary>
    internal Func<Task>? WaitForClose { get; set; }

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        args = AbsolutizePaths(args);
        var waitOnExit = args.Contains(ElevatedChildFlag, StringComparer.Ordinal)
            || args.Contains(WaitOnExitFlag, StringComparer.Ordinal);

        var exit = await ExecuteAsync(args, ct);

        if (waitOnExit && exit != ExitCodes.RebootPending)
        {
            _env.Out.WriteLine(Strings.Get("Elevated_Close"));
            if (WaitForClose is not null)
                await WaitForClose();
            else if (!Console.IsInputRedirected)
                Console.ReadLine();
        }

        return exit;
    }

    private async Task<int> ExecuteAsync(string[] args, CancellationToken ct)
    {
        var verbose = new Option<bool>("--verbose") { Description = Strings.Get("Opt_Verbose"), Recursive = true };
        var elevatedChild = new Option<bool>(ElevatedChildFlag) { Hidden = true, Recursive = true };
        var waitOnExit = new Option<bool>(WaitOnExitFlag) { Hidden = true, Recursive = true };
        var root = new RootCommand(Strings.Get("Cmd_Root")) { Options = { verbose, elevatedChild, waitOnExit } };
        var context = new CommandContext(_env, _modules, _catalog, _cleanup, _issues, _programs, RelaunchArgs(args));

        root.Subcommands.Add(HardwareCommand.Create(context));
        root.Subcommands.Add(DriversCommands.Create(context));
        root.Subcommands.Add(CleanupCommands.Create(context));
        root.Subcommands.Add(ProgramsCommands.Create(context));
        root.Subcommands.Add(DevCommands.Create(context));
        root.Subcommands.Add(PrepareCommands.Create(context));
        root.Subcommands.Add(MediaCommands.Create(context));
        root.Subcommands.Add(RunCommand.Create(context));

        var parse = root.Parse(args);
        if (parse.Errors.Count > 0)
        {
            foreach (var error in parse.Errors)
                _env.Out.WriteLine(error.Message);
            return ExitCodes.InvalidInput;
        }

        // Exceptions and Ctrl+C are handled here, not by System.CommandLine's defaults.
        var config = new InvocationConfiguration
        {
            Output = _env.Out,
            Error = _env.Out,
            EnableDefaultExceptionHandler = false,
            ProcessTerminationTimeout = null,
        };
        try
        {
            return await parse.InvokeAsync(config, ct);
        }
        catch (OperationCanceledException)
        {
            _env.Out.WriteLine(Strings.Get("Cancelled"));
            return ExitCodes.Cancelled;
        }
        catch (Exception ex)
        {
            _env.Log.Error(ex, "Unhandled error");
            _env.Out.WriteLine(Strings.Format("Error_Unexpected", ex.Message));
            return ExitCodes.CompletedWithFailures;
        }
    }

    private static string[] RelaunchArgs(string[] args) =>
        args.Contains(ElevatedChildFlag, StringComparer.Ordinal) ? args : [.. args, ElevatedChildFlag];

    /// <summary>
    /// Makes --profile, --output and --iso absolute: an elevated child starts in System32, and the resume
    /// task starts elsewhere, so relative paths would break.
    /// </summary>
    internal static string[] AbsolutizePaths(string[] args)
    {
        var result = new string[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (i > 0 && args[i - 1] is "--profile" or "--output" or "--iso")
            {
                result[i] = SafeFullPath(arg);
                continue;
            }

            var eq = arg.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && arg[..eq] is "--profile" or "--output" or "--iso")
            {
                result[i] = arg[..(eq + 1)] + SafeFullPath(arg[(eq + 1)..]);
                continue;
            }

            result[i] = arg;
        }

        return result;
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}

internal sealed record CommandContext(
    CliEnvironment Env,
    IReadOnlyList<IModule> Modules,
    VendorCatalog Catalog,
    CleanupCatalog Cleanup,
    KnownIssues Issues,
    ProgramCatalog Programs,
    string[] RelaunchArgs)
{
    public TextWriter Out => Env.Out;

    /// <summary>
    /// Returns null when the process is already elevated (the caller proceeds); otherwise relaunches
    /// elevated and returns the exit code to use.
    /// </summary>
    public async Task<int?> EnsureElevatedAsync()
    {
        if (Env.Elevation.IsElevated)
            return null;

        var code = await Env.Elevation.RelaunchElevatedAsync(RelaunchArgs);
        if (code is null)
        {
            Out.WriteLine(Strings.Get("Elevation_Declined"));
            return ExitCodes.ElevationDeclined;
        }

        var meaning = Strings.Get("ExitMeaning_" + code.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (meaning.StartsWith("ExitMeaning_", StringComparison.Ordinal))
            meaning = Strings.Get("ExitMeaning_Other");
        Out.WriteLine(Strings.Format("Elevated_Result", code.Value, meaning, AppPaths.Default.ReportsDirectory));
        return code;
    }
}
