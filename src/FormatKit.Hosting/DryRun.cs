using FormatKit.Cleanup.Steps;
using FormatKit.Core.Steps;
using FormatKit.Hosting.Localization;
using FormatKit.Programs.Catalog;
using FormatKit.Programs.Steps;

namespace FormatKit.Hosting;

/// <summary>What the dry-run plan lists inside a step: the cleanup items and the programs it would apply.</summary>
public static class DryRun
{
    /// <summary>One text per cleanup item or program of <paramref name="step"/>; empty for any other step.</summary>
    public static IEnumerable<string> Items(IStep step)
    {
        switch (step)
        {
            case CleanupCategoryStep cleanup:
                foreach (var item in cleanup.Items)
                {
                    var level = item.Level.ToString().ToLowerInvariant();
                    var contested = item.Contested ? $" [{Strings.Get("Marker_Contested")}]" : "";
                    yield return $"{item.Id}  {item.LocalizedText().Title} [{level}]{contested}";
                }

                break;
            case WingetProgramsStep winget:
                foreach (var p in winget.Programs)
                {
                    var user = p.Package.Context == InstallContext.User ? $" [{Strings.Get("Marker_User")}]" : "";
                    yield return $"{p.Key}  {p.DisplayName}{user}";
                }

                break;
            case DirectProgramsStep direct:
                foreach (var e in direct.Entries)
                    yield return $"{e.Name} ({e.Url ?? e.Path})";
                break;
        }
    }
}
