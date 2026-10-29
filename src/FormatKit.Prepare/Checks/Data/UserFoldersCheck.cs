using FormatKit.Core.SystemAccess;
using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Checks.Data;

/// <summary>Sizes the user's known folders (redirections followed) and says which ones a reinstall would erase.</summary>
public sealed class UserFoldersCheck : IPrepareCheck
{
    public const string CheckId = "data.user-folders";

    private const string ShellFolders = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";

    // Registry value name, default folder name under the profile, resource key suffix.
    private static readonly (string Value, string Default, string Key)[] Folders =
    [
        ("Desktop", "Desktop", "Desktop"),
        ("Personal", "Documents", "Documents"),
        ("My Pictures", "Pictures", "Pictures"),
        ("My Video", "Videos", "Videos"),
        ("My Music", "Music", "Music"),
        ("{374DE290-123F-4565-9164-39C4925E467B}", "Downloads", "Downloads"),
    ];

    public string Id => CheckId;

    public PrepareGroup Group => PrepareGroup.Data;

    public Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
    {
        var oneDrive = context.Services.UserEnvironment.Get("OneDrive");
        var items = new List<PrepareItem>();
        foreach (var (value, defaultName, key) in Folders)
        {
            ct.ThrowIfCancellationRequested();
            var path = ResolvePath(context, value, defaultName);
            var name = Strings.Get($"UserFolders_Name_{key}");
            items.Add(CheckFolder(context, name, path, oneDrive));
        }

        return Task.FromResult<IReadOnlyList<PrepareItem>>(items);
    }

    private PrepareItem CheckFolder(PrepareContext context, string name, string path, string? oneDrive)
    {
        if (IsUnder(path, oneDrive))
            return Item(PrepareStatus.Ok, Strings.Format("UserFolders_OneDrive_Title", name), Strings.Get("UserFolders_OneDrive_Detail"));

        if (!context.Windows.IsLost(path))
            return Item(PrepareStatus.Ok, Strings.Format("UserFolders_Safe_Title", name), Strings.Format("UserFolders_Safe_Detail", path));

        var scan = FolderScan.Run(context.Services.Files, path);
        if (scan.FileCount == 0)
            return Item(PrepareStatus.Ok, Strings.Format("UserFolders_Empty_Title", name), Unreadable(scan));

        var detail = Strings.Format("UserFolders_Lost_Detail", Sizes.Format(scan.Bytes), scan.FileCount, path);
        if (scan.UnreadableFolders > 0)
            detail += " " + Unreadable(scan);
        return Item(PrepareStatus.Attention, Strings.Format("UserFolders_Lost_Title", name), detail, Strings.Format("UserFolders_Lost_Action", name));
    }

    private static string? Unreadable(FolderScan scan) =>
        scan.UnreadableFolders == 0 ? null
        : scan.UnreadableFolders == 1 ? Strings.Get("UserFolders_Unreadable_One")
        : Strings.Format("UserFolders_Unreadable_Many", scan.UnreadableFolders);

    private static string ResolvePath(PrepareContext context, string value, string defaultName)
    {
        var data = context.Services.Registry.Read(ShellFolders, value)?.Data as string;
        return string.IsNullOrWhiteSpace(data)
            ? Path.Combine(context.Env.UserProfile, defaultName)
            : context.Env.Expand(data);
    }

    private static bool IsUnder(string path, string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return false;
        var prefix = root.TrimEnd('\\') + "\\";
        return (path.TrimEnd('\\') + "\\").StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private PrepareItem Item(PrepareStatus status, string title, string? detail, string? action = null) =>
        new(Id, Group, status, title, detail, action);
}
