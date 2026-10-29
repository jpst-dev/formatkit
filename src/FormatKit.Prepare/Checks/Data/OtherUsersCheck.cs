using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Checks.Data;

/// <summary>Finds local accounts other than the current one whose profile folders a reinstall would erase.</summary>
public sealed class OtherUsersCheck : IPrepareCheck
{
    public const string CheckId = "data.other-users";

    private const string ProfileList = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    private static readonly string[] SystemSids = ["S-1-5-18", "S-1-5-19", "S-1-5-20"];

    public string Id => CheckId;

    public PrepareGroup Group => PrepareGroup.Data;

    public Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
    {
        var registry = context.Services.Registry;
        var currentSid = context.Services.UserSession.CurrentUserSid;
        var items = new List<PrepareItem>();
        foreach (var sid in registry.GetSubKeyNames(ProfileList).OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (SystemSids.Contains(sid, StringComparer.OrdinalIgnoreCase)
                || string.Equals(sid, currentSid, StringComparison.OrdinalIgnoreCase))
                continue;

            var raw = registry.Read($@"{ProfileList}\{sid}", "ProfileImagePath")?.Data as string;
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var folder = context.Env.Expand(raw).TrimEnd('\\');
            if (!context.Services.Files.DirectoryExists(folder))
                continue;

            var name = Path.GetFileName(folder);
            var scan = FolderScan.Run(context.Services.Files, folder);
            var detail = scan.UnreadableFolders == 0
                ? Strings.Format("OtherUsers_Detail_Size", name, Sizes.Format(scan.Bytes), folder)
                : Strings.Format("OtherUsers_Detail", name, folder);
            items.Add(new PrepareItem(Id, Group, PrepareStatus.Attention,
                Strings.Format("OtherUsers_Title", name), detail, Strings.Format("OtherUsers_Action", name)));
        }

        return Task.FromResult<IReadOnlyList<PrepareItem>>(items);
    }
}
