using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Checks.Apps;

/// <summary>Lists browsers with a user data folder (bookmarks, saved passwords) that a reinstall erases.</summary>
public sealed class BrowsersCheck : IPrepareCheck
{
    public const string CheckId = "apps.browsers";

    private sealed record Browser(string Name, bool Roaming, string Folder, bool NeedsProfile = false);

    private static readonly Browser[] Known =
    [
        new("Edge", false, @"Microsoft\Edge\User Data"),
        new("Chrome", false, @"Google\Chrome\User Data"),
        new("Brave", false, @"BraveSoftware\Brave-Browser\User Data"),
        new("Vivaldi", false, @"Vivaldi\User Data"),
        new("Opera", true, @"Opera Software\Opera Stable"),
        new("Firefox", true, @"Mozilla\Firefox\Profiles", NeedsProfile: true),
    ];

    public string Id => CheckId;

    public PrepareGroup Group => PrepareGroup.Apps;

    public Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
    {
        var items = new List<PrepareItem>();
        foreach (var browser in Known)
        {
            ct.ThrowIfCancellationRequested();
            var folder = Path.Combine(browser.Roaming ? context.Env.AppData : context.Env.LocalAppData, browser.Folder);
            if (!Present(context, browser, folder))
                continue;
            items.Add(new PrepareItem(Id, Group, PrepareStatus.Attention,
                Strings.Format("Browsers_Title", browser.Name), Strings.Format("Browsers_Detail", folder),
                Strings.Get("Browsers_Action")));
        }

        return Task.FromResult<IReadOnlyList<PrepareItem>>(items);
    }

    private static bool Present(PrepareContext context, Browser browser, string folder)
    {
        var files = context.Services.Files;
        if (!files.DirectoryExists(folder))
            return false;
        return !browser.NeedsProfile || files.EnumerateDirectories(folder).Any();
    }
}
