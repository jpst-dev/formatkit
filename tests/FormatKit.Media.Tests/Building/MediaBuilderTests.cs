using System.Text;
using FormatKit.Core.Profiles;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Media.Building;
using FormatKit.Media.Bundle;
using FormatKit.Media.Setup;
using FormatKit.Media.Unattend;
using Serilog;

namespace FormatKit.Media.Tests.Building;

public class MediaBuilderTests
{
    private const string Iso = @"C:\iso\win11.iso";
    private static readonly MachineLocale Machine = new("en-US", "0409:00000409", "UTC");
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private sealed class Rig
    {
        public readonly InMemoryFileSystem Fs;
        public readonly FakeDiskImages Disk = new();
        public readonly FakeWindowsImages Images;
        public readonly FakeInteractiveConsole Console = new();
        public readonly MediaBuilder Builder;

        public Rig(string image = "install.wim", long imageBytes = 1000, params WindowsImageInfo[] editions)
        {
            (Fs, Images) = InstallMediaTests.Iso(image, imageBytes, editions);
            Fs.Seed(@"C:\exe\formatkit.exe", "EXE");
            Builder = new MediaBuilder(TestServices.Create(files: Fs, diskImages: Disk, windowsImages: Images, console: Console), Log);
        }

        public ProfileBundle Bundle()
        {
            Fs.Seed(@"C:\p\profile.json", """{ "modules": {} }""");
            return ProfileBundle.Plan(new LoadedProfile(null!, @"C:\p\profile.json", "h"), [], Fs);
        }
    }

    private static MediaRequest Request(ProfileBundle? bundle = null, SetupOptions? setup = null) =>
        new(Iso, setup ?? new SetupOptions(), bundle, Machine, @"C:\exe\formatkit.exe");

    [Fact]
    public async Task Prepare_mounts_and_resolves()
    {
        var rig = new Rig();
        await using var media = await rig.Builder.PrepareAsync(Request(), CancellationToken.None);
        Assert.Equal([Iso], rig.Disk.Mounted);
        Assert.Equal(@"E:\", media.MountRoot);
        Assert.Equal("en-US", media.Setup.Language);
        Assert.Empty(rig.Disk.Dismounted);
    }

    [Fact]
    public async Task Prepare_unknown_edition_names_setup_edition_and_dismounts()
    {
        var rig = new Rig();
        var ex = await Assert.ThrowsAsync<MediaException>(() =>
            rig.Builder.PrepareAsync(Request(setup: new SetupOptions(Edition: "Windows 11 Home")), CancellationToken.None));
        Assert.Contains("setup.edition", ex.Message);
        Assert.Contains("Windows 11 Pro", ex.Message);
        Assert.Equal([Iso], rig.Disk.Dismounted);
    }

    [Fact]
    public async Task Prepare_language_error_dismounts()
    {
        var rig = new Rig();
        var ex = await Assert.ThrowsAsync<MediaException>(() =>
            rig.Builder.PrepareAsync(Request(setup: new SetupOptions(Language: "pt-BR")), CancellationToken.None));
        Assert.Contains("setup.language", ex.Message);
        Assert.Equal([Iso], rig.Disk.Dismounted);
    }

    [Fact]
    public async Task Prepare_uses_the_requested_edition_languages()
    {
        var rig = new Rig(editions:
        [
            InstallMediaTests.Edition("Windows 11 Home", languages: ["en-US"]),
            InstallMediaTests.Edition("Windows 11 Pro", languages: ["en-US", "pt-BR"]),
        ]);
        await using var media = await rig.Builder.PrepareAsync(
            Request(setup: new SetupOptions(Edition: "Windows 11 Pro", Language: "pt-BR")), CancellationToken.None);
        Assert.Equal("pt-BR", media.Setup.Language);
    }

    [Fact]
    public async Task Prepare_failed_mount_is_a_media_exception()
    {
        var rig = new Rig();
        rig.Disk.ThrowOnMount = new InvalidOperationException("boom");
        await Assert.ThrowsAsync<MediaException>(() => rig.Builder.PrepareAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Prepare_required_bytes_is_tree_plus_bundle_plus_exe_plus_one_percent()
    {
        var rig = new Rig(imageBytes: 100_000);
        var bundle = rig.Bundle();
        var tree = Encoding.UTF8.GetByteCount("efi" + "sdi" + "setup" + "bootwim") + 100_000;
        await using (var plain = await rig.Builder.PrepareAsync(Request(), CancellationToken.None))
            Assert.Equal(tree + (tree + 99) / 100, plain.RequiredBytes);

        var total = tree + bundle.TotalBytes + 3;
        await using var media = await rig.Builder.PrepareAsync(Request(bundle), CancellationToken.None);
        Assert.Equal(total + (total + 99) / 100, media.RequiredBytes);
    }

    [Fact]
    public async Task Prepare_esd_over_fat32_limit_is_rejected_and_dismounted()
    {
        var rig = new Rig("install.esd", MediaBuilder.Fat32MaxFile + 1);
        await Assert.ThrowsAsync<MediaException>(() => rig.Builder.PrepareAsync(Request(), CancellationToken.None));
        Assert.Equal([Iso], rig.Disk.Dismounted);
    }

    [Fact]
    public async Task Write_copies_tree_and_writes_answer_file()
    {
        var rig = new Rig();
        await using var media = await rig.Builder.PrepareAsync(Request(), CancellationToken.None);
        var progress = new List<MediaProgress>();
        await rig.Builder.WriteAsync(media, Request(), @"G:\out", new SyncProgress(progress), CancellationToken.None);

        Assert.True(rig.Fs.FileExists(@"G:\out\setup.exe"));
        Assert.True(rig.Fs.FileExists(@"G:\out\efi\boot\bootx64.efi"));
        Assert.True(rig.Fs.FileExists(@"G:\out\sources\boot.wim"));
        Assert.Contains(rig.Fs.EnumerateFileEntries(@"G:\out\sources", false), e => e.Path.EndsWith("install.wim"));
        Assert.Empty(rig.Images.Splits);
        Assert.Equal(UnattendBuilder.Build(media.Setup, false), rig.Fs.ReadAllText(@"G:\out\autounattend.xml"));
        Assert.Equal(MediaPhase.Done, progress[^1].Phase);
        Assert.Equal(progress[^1].TotalBytes, progress[^1].CopiedBytes);
        Assert.Equal(media.Image.Bytes + 3 + 3 + 5 + 7, progress[^1].TotalBytes);
    }

    [Fact]
    public async Task Write_answer_file_is_utf8_bytes()
    {
        var rig = new Rig();
        await using var media = await rig.Builder.PrepareAsync(Request(), CancellationToken.None);
        await rig.Builder.WriteAsync(media, Request(), @"G:\out", null, CancellationToken.None);
        Assert.Equal(Encoding.UTF8.GetBytes(UnattendBuilder.Build(media.Setup, false)), rig.Fs.RawBytes[@"G:\out\autounattend.xml"]);
    }

    [Fact]
    public async Task Write_splits_image_over_the_limit()
    {
        var rig = new Rig(imageBytes: MediaBuilder.Fat32MaxFile + 1);
        await using var media = await rig.Builder.PrepareAsync(Request(), CancellationToken.None);
        await rig.Builder.WriteAsync(media, Request(), @"G:\out", null, CancellationToken.None);
        Assert.Equal([(@"E:\sources\install.wim", @"G:\out\sources\install.swm", 3800)], rig.Images.Splits);
        Assert.DoesNotContain(rig.Fs.EnumerateFileEntries(@"G:\out", true), e => e.Path.EndsWith("install.wim"));
        Assert.True(rig.Fs.FileExists(@"G:\out\sources\install2.swm"));
    }

    [Fact]
    public async Task Write_copies_image_of_exactly_the_limit()
    {
        var rig = new Rig(imageBytes: MediaBuilder.Fat32MaxFile);
        await using var media = await rig.Builder.PrepareAsync(Request(), CancellationToken.None);
        await rig.Builder.WriteAsync(media, Request(), @"G:\out", null, CancellationToken.None);
        Assert.Empty(rig.Images.Splits);
        Assert.Contains(rig.Fs.EnumerateFileEntries(@"G:\out\sources", false), e => e.Path.EndsWith("install.wim"));
    }

    [Fact]
    public async Task Write_with_bundle_puts_formatkit_under_the_oem_folder()
    {
        var rig = new Rig();
        var req = Request(rig.Bundle());
        await using var media = await rig.Builder.PrepareAsync(req, CancellationToken.None);
        await rig.Builder.WriteAsync(media, req, @"G:\out", null, CancellationToken.None);
        Assert.True(rig.Fs.FileExists(@"G:\out\sources\$OEM$\$1\FormatKit\formatkit.exe"));
        Assert.True(rig.Fs.FileExists(@"G:\out\sources\$OEM$\$1\FormatKit\profile.json"));
        Assert.Equal(UnattendBuilder.Build(media.Setup, true), rig.Fs.ReadAllText(@"G:\out\autounattend.xml"));
    }

    [Fact]
    public async Task Dispose_dismounts_even_after_write_failed()
    {
        var rig = new Rig(imageBytes: MediaBuilder.Fat32MaxFile + 1);
        rig.Images.ThrowOnSplit = new InvalidOperationException("split failed");
        var media = await rig.Builder.PrepareAsync(Request(), CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Builder.WriteAsync(media, Request(), @"G:\out", null, CancellationToken.None));
        await media.DisposeAsync();
        Assert.Equal([Iso], rig.Disk.Dismounted);
    }

    [Fact]
    public async Task Dismount_failure_prints_the_manual_command_and_does_not_throw()
    {
        var rig = new Rig();
        var media = await rig.Builder.PrepareAsync(Request(), CancellationToken.None);
        rig.Disk.ThrowOnDismount = new InvalidOperationException("busy");
        await media.DisposeAsync();
        Assert.Contains(rig.Console.Lines, l => l.Contains(@"Dismount-DiskImage -ImagePath 'C:\iso\win11.iso'"));
    }

    [Fact]
    public async Task Dismount_failure_does_not_hide_the_original_exception()
    {
        var rig = new Rig();
        rig.Disk.ThrowOnDismount = new InvalidOperationException("busy");
        var ex = await Assert.ThrowsAsync<MediaException>(() =>
            rig.Builder.PrepareAsync(Request(setup: new SetupOptions(Edition: "nope")), CancellationToken.None));
        Assert.Contains("setup.edition", ex.Message);
    }

    [Fact]
    public async Task Write_honors_cancellation()
    {
        var rig = new Rig();
        await using var media = await rig.Builder.PrepareAsync(Request(), CancellationToken.None);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            rig.Builder.WriteAsync(media, Request(), @"G:\out", null, cts.Token));
    }

    private sealed class SyncProgress(List<MediaProgress> list) : IProgress<MediaProgress>
    {
        public void Report(MediaProgress value) => list.Add(value);
    }
}
