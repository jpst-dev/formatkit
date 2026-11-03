using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Media.Localization;

namespace FormatKit.Media.Building;

public sealed record InstallImage(string Path, bool IsEsd, long Bytes, IReadOnlyList<WindowsImageInfo> Editions);

/// <summary>Checks that a mounted ISO is Windows 11 x64 UEFI install media and reads its editions.</summary>
public static class InstallMedia
{
    private const int Amd64 = 9;
    private const int FirstWindows11Build = 22000;

    public static async Task<InstallImage> InspectAsync(string root, ServiceSet services, CancellationToken ct)
    {
        var files = services.Files;
        var sources = System.IO.Path.Combine(root, "sources");
        var wim = Find(files, sources, "install.wim");
        var esd = wim is null ? Find(files, sources, "install.esd") : null;
        var found = wim ?? esd ?? throw new MediaException(Strings.Get("Media_NoInstallImage"));

        if (Find(files, System.IO.Path.Combine(root, "efi", "boot"), "bootx64.efi") is null)
            throw new MediaException(Strings.Get("Media_NoBootFile"));

        IReadOnlyList<WindowsImageInfo> editions;
        try
        {
            editions = await services.WindowsImages.ListAsync(found.Path, ct);
        }
        catch (InvalidOperationException e)
        {
            throw new MediaException(Strings.Format("Media_ImageUnreadable", e.Message));
        }

        if (editions.Count == 0)
            throw new MediaException(Strings.Get("Media_NoEditions"));
        foreach (var edition in editions)
        {
            if (edition.Architecture != Amd64)
                throw new MediaException(Strings.Format("Media_NotAmd64", edition.Name));
            if (!Version.TryParse(edition.Version, out var version) || version.Major != 10 || version.Build < FirstWindows11Build)
                throw new MediaException(Strings.Format("Media_NotWindows11", edition.Name, edition.Version));
            if (edition.Languages.Count == 0)
                throw new MediaException(Strings.Format("Media_NoLanguages", edition.Name));
        }

        return new InstallImage(found.Path, wim is null, found.Length, editions);
    }

    private static FileEntry? Find(IFileSystem files, string dir, string name) =>
        files.EnumerateFileEntries(dir, recursive: false)
            .FirstOrDefault(e => string.Equals(System.IO.Path.GetFileName(e.Path), name, StringComparison.OrdinalIgnoreCase));
}
