using System.CommandLine;
using FormatKit.Cli.Localization;
using FormatKit.Core;
using FormatKit.Core.Capture;
using FormatKit.Dev;
using FormatKit.Hosting;

namespace FormatKit.Cli.Commands;

internal static class ProfileCommands
{
    public static Command Create(CommandContext context)
    {
        var profile = new Command("profile", Strings.Get("Cmd_Profile"));
        profile.Subcommands.Add(CreateCapture(context));
        return profile;
    }

    private static Command CreateCapture(CommandContext context)
    {
        var file = new Argument<string>("file") { Description = Strings.Get("Arg_ProfileFile") };
        var force = new Option<bool>("--force") { Description = Strings.Get("Opt_ProfileForce") };
        var capture = new Command("capture", Strings.Get("Cmd_ProfileCapture")) { Arguments = { file }, Options = { force } };

        capture.SetAction(async (parse, ct) =>
        {
            var o = context.Out;
            string full;
            try
            {
                full = Path.GetFullPath(parse.GetValue(file)!);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                o.WriteLine(Strings.Format("Profile_BadPath", parse.GetValue(file)!));
                return ExitCodes.InvalidInput;
            }

            var services = context.Env.Services;
            if (services.Files.FileExists(full) && !parse.GetValue(force))
            {
                o.WriteLine(Strings.Format("Profile_Exists", full));
                return ExitCodes.InvalidInput;
            }

            var machine = PrepareCommands.Environment();
            var sources = ProfileCapture.RealSources(
                services, context.Programs, context.Cleanup, context.Env.Dev ?? DevEnvironment.Current(),
                machine.SystemDrive, Path.GetTempPath(), AppPaths.Default, context.Env.Log);
            var runner = new ProfileCapture(context.Modules, services.Files, services.Volumes, sources, machine.ComputerName, machine.SystemDrive);

            var result = await runner.RunAsync(ct);
            if (result.Json is null)
            {
                o.WriteLine(Strings.Get("Profile_Invalid"));
                foreach (var error in result.Errors)
                    o.WriteLine($"  {error.Path}: {error.Message}");
                return ExitCodes.CompletedWithFailures;
            }

            var write = await runner.WriteAsync(result, full, ct);
            if (write.Error is not null)
            {
                o.WriteLine(Strings.Format("Profile_WriteFailed", full, write.Error));
                return ExitCodes.CompletedWithFailures;
            }

            WriteReport(o, result);
            o.WriteLine();
            o.WriteLine(Strings.Format("Profile_Written", full));
            if (write.OnWindowsDrive)
                o.WriteLine(Strings.Get("Profile_OnWindowsDrive"));
            o.WriteLine(Strings.Get("Profile_PersonalData"));
            return ExitCodes.Ok;
        });
        return capture;
    }

    private static void WriteReport(TextWriter o, ProfileCaptureResult result)
    {
        o.WriteLine(Strings.Get("Profile_Captured"));
        foreach (var section in result.Sections)
            o.WriteLine($"  {section.Section}: {section.Text}");

        WriteNotes(o, result, CaptureNoteKind.NotCaptured, "Profile_NotCaptured");
        WriteNotes(o, result, CaptureNoteKind.Review, "Profile_Review");
    }

    private static void WriteNotes(TextWriter o, ProfileCaptureResult result, CaptureNoteKind kind, string title)
    {
        var notes = result.Notes.Where(n => n.Kind == kind).ToList();
        if (notes.Count == 0)
            return;

        o.WriteLine();
        o.WriteLine(Strings.Get(title));
        foreach (var note in notes)
            o.WriteLine($"  {note.Path}: {note.Text}");
    }
}
