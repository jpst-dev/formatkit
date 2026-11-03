using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Media.Building;

namespace FormatKit.Media.Tests.Building;

public class InstallMediaTests
{
    internal static WindowsImageInfo Edition(string name = "Windows 11 Pro", int arch = 9, string version = "10.0.26100.1742",
        string[]? languages = null) =>
        new(1, name, arch, version, languages ?? ["en-US"], 0);

    internal static (InMemoryFileSystem Fs, FakeWindowsImages Images) Iso(string image = "install.wim",
        long imageBytes = 1000, params WindowsImageInfo[] editions)
    {
        var fs = new InMemoryFileSystem();
        fs.Seed(@"E:\efi\boot\bootx64.efi", "efi");
        fs.Seed(@"E:\boot\boot.sdi", "sdi");
        fs.Seed(@"E:\setup.exe", "setup");
        fs.Seed(@"E:\sources\boot.wim", "bootwim");
        fs.SeedEntry(@"E:\sources\" + image, imageBytes);
        var images = new FakeWindowsImages(fs);
        images.Images[@"E:\sources\" + image] = editions.Length > 0 ? editions : [Edition()];
        return (fs, images);
    }

    private static Task<InstallImage> Inspect(InMemoryFileSystem fs, FakeWindowsImages images) =>
        InstallMedia.InspectAsync(@"E:\", TestServices.Create(files: fs, windowsImages: images), CancellationToken.None);

    [Fact]
    public async Task Valid_tree_gives_image_with_editions()
    {
        var (fs, images) = Iso(imageBytes: 5000);
        var image = await Inspect(fs, images);
        Assert.Equal(@"E:\sources\install.wim", image.Path);
        Assert.False(image.IsEsd);
        Assert.Equal(5000, image.Bytes);
        Assert.Equal(["Windows 11 Pro"], image.Editions.Select(e => e.Name));
    }

    [Fact]
    public async Task Missing_install_image_is_rejected()
    {
        var (_, images) = Iso();
        var empty = new InMemoryFileSystem();
        empty.Seed(@"E:\efi\boot\bootx64.efi", "x");
        var ex = await Assert.ThrowsAsync<MediaException>(() => Inspect(empty, images));
        Assert.Contains("install.wim", ex.Message);
    }

    [Fact]
    public async Task Missing_bootx64_is_rejected()
    {
        var (fs, images) = Iso();
        fs.Delete(@"E:\efi\boot\bootx64.efi");
        var ex = await Assert.ThrowsAsync<MediaException>(() => Inspect(fs, images));
        Assert.Contains("bootx64.efi", ex.Message);
    }

    [Fact]
    public async Task Arm64_edition_is_rejected()
    {
        var (fs, images) = Iso(editions: Edition("Windows 11 Pro", arch: 12));
        var ex = await Assert.ThrowsAsync<MediaException>(() => Inspect(fs, images));
        Assert.Contains("Windows 11 Pro", ex.Message);
    }

    [Fact]
    public async Task Windows_10_is_rejected()
    {
        var (fs, images) = Iso(editions: Edition(version: "10.0.19045.1"));
        await Assert.ThrowsAsync<MediaException>(() => Inspect(fs, images));
    }

    [Fact]
    public async Task Edition_without_languages_is_rejected()
    {
        var (fs, images) = Iso(editions: Edition(languages: []));
        await Assert.ThrowsAsync<MediaException>(() => Inspect(fs, images));
    }

    [Fact]
    public async Task Esd_is_used_only_when_there_is_no_wim()
    {
        var (fs, images) = Iso("install.esd");
        var image = await Inspect(fs, images);
        Assert.True(image.IsEsd);
        Assert.Equal(@"E:\sources\install.esd", image.Path);

        fs.SeedEntry(@"E:\sources\install.wim", 10);
        images.Images[@"E:\sources\install.wim"] = [Edition("Windows 11 Home")];
        var both = await Inspect(fs, images);
        Assert.False(both.IsEsd);
        Assert.Equal("Windows 11 Home", both.Editions[0].Name);
    }
}
