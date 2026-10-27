using System.CommandLine;
using FormatKit.Cli.Commands;
using FormatKit.Cli.Localization;
using FormatKit.Core.Profiles;
using FormatKit.Drivers;
using FormatKit.Drivers.Catalog;

namespace FormatKit.Cli;

public sealed class CliApp
{
    private readonly CliEnvironment _env;
    private readonly IReadOnlyList<IModule> _modules;
    private readonly VendorCatalog _catalog;

    public CliApp(CliEnvironment env)
        : this(env, VendorCatalog.LoadEmbedded(), null)
    {
    }

    /// <summary>Test seam: lets tests plan with fake modules.</summary>
    internal CliApp(CliEnvironment env, VendorCatalog catalog, IReadOnlyList<IModule>? modules)
    {
        _env = env;
        _catalog = catalog;
        _modules = modules ?? [new DriversModule(catalog, env.NetworkRetryDelay)];
    }

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var verbose = new Option<bool>("--verbose") { Description = Strings.Get("Opt_Verbose"), Recursive = true };
        var root = new RootCommand(Strings.Get("Cmd_Root")) { Options = { verbose } };
        var context = new CommandContext(_env, _modules, _catalog, args);

        root.Subcommands.Add(HardwareCommand.Create(context));
        root.Subcommands.Add(DriversCommands.Create(context));
        root.Subcommands.Add(RunCommand.Create(context));

        var parse = root.Parse(args);
        if (parse.Errors.Count > 0)
        {
            foreach (var error in parse.Errors)
                _env.Out.WriteLine(error.Message);
            return ExitCodes.InvalidInput;
        }

        var config = new InvocationConfiguration { Output = _env.Out, Error = _env.Out };
        try
        {
            return await parse.InvokeAsync(config, ct);
        }
        catch (OperationCanceledException)
        {
            _env.Out.WriteLine(Strings.Get("Cancelled"));
            return ExitCodes.Cancelled;
        }
    }
}

internal sealed record CommandContext(
    CliEnvironment Env,
    IReadOnlyList<IModule> Modules,
    VendorCatalog Catalog,
    string[] Args)
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

        var code = await Env.Elevation.RelaunchElevatedAsync(Args);
        if (code is null)
        {
            Out.WriteLine(Strings.Get("Elevation_Declined"));
            return ExitCodes.ElevationDeclined;
        }

        return code;
    }
}
