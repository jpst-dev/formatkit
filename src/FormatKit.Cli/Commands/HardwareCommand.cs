using System.CommandLine;
using System.Management;
using System.Runtime.InteropServices;
using FormatKit.Cli.Localization;
using FormatKit.Core.Hardware;

namespace FormatKit.Cli.Commands;

internal static class HardwareCommand
{
    public static Command Create(CommandContext context)
    {
        var command = new Command("hardware", Strings.Get("Cmd_Hardware"));
        command.SetAction(async (_, ct) => await Output.GuardSystemQueryAsync(context.Out, async () =>
        {
            var snapshot = await context.Env.Services.Hardware.ReadAsync(ct);
            Print(context.Out, snapshot);
            return ExitCodes.Ok;
        }));
        return command;
    }

    internal static void Print(TextWriter o, HardwareSnapshot hw)
    {
        var unknown = Strings.Get("Hw_Unknown");
        Output.Field(o, Strings.Get("Hw_Machine"), $"{hw.Machine.Manufacturer} {hw.Machine.Model}".Trim());
        Output.Field(o, Strings.Get("Hw_Board"), $"{hw.Board.Manufacturer} {hw.Board.Product}".Trim());
        Output.Field(o, Strings.Get("Hw_Bios"), hw.BiosVersion);
        Output.List(o, Strings.Get("Hw_Gpus"),
            hw.Gpus.Select(g => Strings.Format("Hw_Gpu", g.Name, g.DriverVersion ?? unknown)).ToList());
        Output.List(o, Strings.Get("Hw_ProblemDevices"),
            hw.ProblemDevices.Select(d => Strings.Format("Hw_Problem", d.DisplayName, d.ErrorCode)).ToList());
        Output.List(o, Strings.Get("Hw_NetworkAdapters"),
            hw.NetworkAdapters.Select(a => Strings.Format("Hw_Adapter", a.Name, a.InfName ?? unknown, a.DriverVersion ?? unknown)).ToList());
    }
}

internal static class Output
{
    private const int LabelWidth = 30;

    public static void Field(TextWriter o, string label, string value) =>
        o.WriteLine($"{(label + ":").PadRight(LabelWidth)}{value}");

    public static void List(TextWriter o, string label, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            Field(o, label, Strings.Get("Hw_None"));
            return;
        }

        Field(o, label, items[0]);
        foreach (var item in items.Skip(1))
            o.WriteLine(new string(' ', LabelWidth) + item);
    }

    /// <summary>Runs a WMI/COM-backed query, turning access and query failures into a localized error (exit code 1).</summary>
    public static async Task<int> GuardSystemQueryAsync(TextWriter o, Func<Task<int>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            o.WriteLine(Strings.Format("Error_Wmi", ex.Message));
            return ExitCodes.CompletedWithFailures;
        }
    }
}
