using FormatKit.Cleanup;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Diagnosis;
using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;
using FormatKit.Dev;
using FormatKit.Drivers;
using FormatKit.Drivers.Catalog;
using FormatKit.Programs;
using FormatKit.Programs.Catalog;

namespace FormatKit.Hosting;

/// <summary>The modules a run plans with, plus the catalogs they were built from.</summary>
public sealed record ModuleSet(
    IReadOnlyList<IModule> Modules,
    VendorCatalog Vendors,
    CleanupCatalog Cleanup,
    KnownIssues Issues,
    ProgramCatalog Programs)
{
    /// <summary>Builds the real modules, in plan order: drivers, cleanup, programs, dev.</summary>
    public static ModuleSet CreateReal(ServiceSet services, Serilog.ILogger log, TimeSpan networkRetryDelay, DevEnvironment? dev)
    {
        var vendors = VendorCatalog.LoadEmbedded();
        var cleanup = CleanupCatalog.LoadEmbedded();
        var issues = KnownIssues.LoadEmbedded();
        var programs = ProgramCatalog.LoadEmbedded();
        IReadOnlyList<IModule> modules =
        [
            new DriversModule(vendors, networkRetryDelay),
            new CleanupModule(cleanup, AppPaths.Default, services.Files, log),
            new ProgramsModule(programs, AppPaths.Default, services.Files, log),
            new DevModule(TimeProvider.System, dev),
        ];
        return new ModuleSet(modules, vendors, cleanup, issues, programs);
    }
}
