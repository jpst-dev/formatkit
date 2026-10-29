using System.Text.Json;
using FormatKit.Prepare.Localization;

namespace FormatKit.Prepare.Checks.Developer;

/// <summary>Finds SSH private keys (OpenSSH, PEM and PuTTY .ppk) and GPG secret keys that a reinstall would erase. Reports file names and counts only; a file is read no further than its first line.</summary>
public sealed class KeysCheck : IPrepareCheck
{
    public const string CheckId = "dev.keys";

    private const string PrivateKeyMarker = "PRIVATE KEY";

    /// <summary>First line of a PuTTY private key (.ppk), versions 2 and 3.</summary>
    private const string PuttyKeyMarker = "PuTTY-User-Key-File-";

    /// <summary>A PEM header is far shorter; this bounds what is read from a file that has no line break.</summary>
    private const int FirstLineLimit = 128;

    private sealed class Tally { public int Count; }

    public string Id => CheckId;

    public PrepareGroup Group => PrepareGroup.Developer;

    public Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
    {
        var unreadable = new Tally();
        var items = new List<PrepareItem>();

        var sshDir = Path.Combine(context.Env.UserProfile, ".ssh");
        var sshKeys = Folder(context, sshDir, ct, unreadable)
            .Where(f => !f.Path.EndsWith(".pub", StringComparison.OrdinalIgnoreCase))
            .Where(f => IsPrivateKey(context, f.Path, unreadable))
            .Select(f => Path.GetFileName(f.Path))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sshKeys.Count > 0)
            items.Add(SshItem(context, sshKeys));

        var gpgDir = Path.Combine(context.Env.AppData, "gnupg", "private-keys-v1.d");
        var gpgCount = Folder(context, gpgDir, ct, unreadable).Count();
        if (gpgCount > 0)
            items.Add(new PrepareItem(Id, Group, PrepareStatus.Attention,
                gpgCount == 1 ? Strings.Get("Gpg_Title_One") : Strings.Format("Gpg_Title_Many", gpgCount),
                Strings.Format("Gpg_Detail", gpgDir), Strings.Get("Gpg_Action")));

        if (unreadable.Count > 0)
        {
            context.Log.Debug("{Count} key folders or files could not be read", unreadable.Count);
            items.Add(new PrepareItem(Id, Group, PrepareStatus.NotChecked,
                Strings.Get("Keys_Unreadable_Title"), Strings.Get("Keys_Unreadable_Detail"), Strings.Get("Keys_Unreadable_Action")));
        }

        return Task.FromResult<IReadOnlyList<PrepareItem>>(items);
    }

    private PrepareItem SshItem(PrepareContext context, List<string> names) =>
        new(Id, Group, PrepareStatus.Attention,
            names.Count == 1 ? Strings.Get("Keys_Title_One") : Strings.Format("Keys_Title_Many", names.Count),
            Strings.Format("Keys_Detail", string.Join(", ", names)),
            Strings.Get(ProfileCreatesSshKey(context) ? "Keys_Action_Profile" : "Keys_Action_Backup"));

    /// <summary>The files directly inside a folder that a reinstall erases; a missing or unreadable folder yields none.</summary>
    private static IEnumerable<FormatKit.Core.SystemAccess.FileEntry> Folder(
        PrepareContext context, string dir, CancellationToken ct, Tally unreadable)
    {
        ct.ThrowIfCancellationRequested();
        if (!context.Windows.MayBeLost(dir) || !context.Services.Files.DirectoryExists(dir))
            return [];
        var failed = 0;
        var files = context.Services.Files.EnumerateFileEntries(dir, recursive: false, _ => failed++).ToList();
        unreadable.Count += failed;
        return files;
    }

    private static bool IsPrivateKey(PrepareContext context, string path, Tally unreadable)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("id_", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            using var stream = context.Services.Files.OpenRead(path);
            using var reader = new StreamReader(stream);
            var buffer = new char[FirstLineLimit];
            var read = reader.Read(buffer, 0, buffer.Length);
            var firstLine = new string(buffer, 0, read).Split('\n')[0];
            return firstLine.Contains(PrivateKeyMarker, StringComparison.Ordinal)
                || firstLine.StartsWith(PuttyKeyMarker, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            unreadable.Count++;
            return false;
        }
    }

    /// <summary>R3: the profile has <c>dev.ssh</c> when its <c>dev</c> section is enabled and has an <c>ssh</c> property.</summary>
    private static bool ProfileCreatesSshKey(PrepareContext context) =>
        context.Profile?.Profile.Modules.TryGetValue("dev", out var dev) == true
        && dev.ValueKind == JsonValueKind.Object
        && dev.TryGetProperty("enabled", out var enabled)
        && enabled.ValueKind == JsonValueKind.True
        && dev.TryGetProperty("ssh", out _);
}
