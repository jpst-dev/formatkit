using System.CommandLine;
using FormatKit.Cli.Localization;
using FormatKit.Core.Profiles;
using FormatKit.Drivers;

namespace FormatKit.Cli.Commands;

internal static class DriversCommands
{
    public static Command Create(CommandContext context)
    {
        var drivers = new Command("drivers", Strings.Get("Cmd_Drivers"));
        drivers.Subcommands.Add(CreateScan(context));
        drivers.Subcommands.Add(CreateInstall(context));
        drivers.Subcommands.Add(CreateBackup(context));
        return drivers;
    }

    private static Command CreateScan(CommandContext context)
    {
        var scan = new Command("scan", Strings.Get("Cmd_Scan"));
        scan.SetAction(async (_, ct) => await Output.GuardSystemQueryAsync(context.Out, async () =>
        {
            var report = await new DriverScanner(context.Env.Services, context.Catalog).ScanAsync(ct);
            PrintScan(context.Out, report);
            return ExitCodes.Ok;
        }));
        return scan;
    }

    private static void PrintScan(TextWriter o, DriverScanReport report)
    {
        HardwareCommand.Print(o, report.Hardware);
        o.WriteLine();

        var machine = report.Machine.Oem is { } oem
            ? Strings.Format("Scan_Oem", oem.Id)
            : report.Machine.Board is { } board
                ? Strings.Format("Scan_Board", board.SupportUrl)
                : Strings.Get("Scan_Custom");
        Output.Field(o, Strings.Get("Scan_MachineType"), machine);
        Output.Field(o, Strings.Get("Scan_Online"), Strings.Get(report.Online ? "Value_Yes" : "Value_No"));
        Output.List(o, Strings.Get("Scan_Updates"), report.AvailableUpdates.Select(u => u.Title).ToList());
        Output.List(o, Strings.Get("Scan_GpuPackages"), report.GpuPackages.Select(p => p.WingetId).ToList());
        if (report.WindowsUpdateError is not null)
            o.WriteLine(Strings.Format("Scan_WuError", report.WindowsUpdateError));
    }

    private static Command CreateInstall(CommandContext context)
    {
        var dryRun = new Option<bool>("--dry-run") { Description = Strings.Get("Opt_DryRun") };
        var install = new Command("install", Strings.Get("Cmd_Install")) { Options = { dryRun } };
        install.SetAction(async (parse, ct) =>
        {
            var profile = new LoadedProfile(
                BuiltInProfiles.DriversOnly, BuiltInProfiles.DriversOnlyPath, BuiltInProfiles.DriversOnlyHash);
            return await RunFlow.ExecuteAsync(context, profile, resume: false, parse.GetValue(dryRun), ct);
        });
        return install;
    }

    private static Command CreateBackup(CommandContext context)
    {
        var output = new Option<string>("--output") { Description = Strings.Get("Opt_Output"), Required = true };
        var backup = new Command("backup-network", Strings.Get("Cmd_Backup")) { Options = { output } };
        backup.SetAction(async (parse, ct) =>
        {
            if (await context.EnsureElevatedAsync() is { } exit)
                return exit;

            var dir = parse.GetValue(output)!;
            return await Output.GuardSystemQueryAsync(context.Out, async () =>
            {
                var result = await new NetworkDriverBackup(context.Env.Services).ExportAsync(dir, ct);
                context.Out.WriteLine(Strings.Format("Backup_Header", dir));
                Output.List(context.Out, Strings.Get("Backup_Exported"), result.Exported);
                Output.List(context.Out, Strings.Get("Backup_Skipped"), result.SkippedInbox);
                Output.List(context.Out, Strings.Get("Backup_Failed"), result.Failed);
                return result.Failed.Count > 0 ? ExitCodes.CompletedWithFailures : ExitCodes.Ok;
            });
        });
        return backup;
    }
}
