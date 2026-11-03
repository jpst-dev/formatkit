using FormatKit.Core.Profiles;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Hosting;
using FormatKit.Media.Setup;

namespace FormatKit.Hosting.Tests;

public sealed class MediaRunnerTests : IDisposable
{
    private const string Iso = @"C:\iso\win11.iso";
    private const string Exe = @"C:\exe\formatkit.exe";
    private const string Valid = """{"schemaVersion":1,"name":"T","modules":{"fake":{"enabled":true}}}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly MachineLocale Machine = new("en-US", "0409:00000409", "UTC");

    private readonly string _dir = Directory.CreateTempSubdirectory("fk-media-runner-").FullName;
    private readonly InMemoryFileSystem _files = new();
    private readonly FakeDiskImages _mounts = new();
    private readonly FakeWindowsImages _images;
    private readonly FakeModule _module = new("fake");

    public MediaRunnerTests()
    {
        _files.Seed(Exe, "EXE");
        _files.Seed(@"E:\efi\boot\bootx64.efi", "efi");
        _files.SeedEntry(@"E:\sources\install.wim", 1000);
        _images = new FakeWindowsImages(_files);
        _images.Images[@"E:\sources\install.wim"] = [new WindowsImageInfo(1, "Windows 11 Pro", 9, "10.0.26100.1742", ["en-US"], 0)];
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private MediaRunner Runner() => new(
        TestServices.Create(files: _files, diskImages: _mounts, windowsImages: _images),
        new Serilog.LoggerConfiguration().CreateLogger(),
        [_module]);

    private string WriteProfile(string json = Valid)
    {
        var path = Path.Combine(_dir, "p.json");
        File.WriteAllText(path, json);
        _files.Seed(path, json);
        return path;
    }

    [Fact]
    public async Task No_profile_mounts_and_has_no_bundle()
    {
        var outcome = await Runner().PrepareAsync(Iso, null, null, Machine, Exe, Ct);

        Assert.NotNull(outcome.Media);
        Assert.Null(outcome.Request!.Bundle);
        await outcome.Media.DisposeAsync();
        Assert.Equal([Iso], _mounts.Dismounted);
    }

    [Fact]
    public async Task Invalid_profile_returns_its_errors_without_mounting()
    {
        var outcome = await Runner().PrepareAsync(Iso, WriteProfile("""{"schemaVersion":1}"""), null, Machine, Exe, Ct);

        Assert.Null(outcome.Media);
        Assert.NotEmpty(outcome.ProfileErrors);
        Assert.Empty(_mounts.Mounted);
    }

    [Fact]
    public async Task Invalid_setup_section_returns_its_errors_without_mounting()
    {
        var path = WriteProfile("""{"schemaVersion":1,"name":"T","modules":{"fake":{"enabled":true}},"setup":{"account":"nobody"}}""");

        var outcome = await Runner().PrepareAsync(Iso, path, null, Machine, Exe, Ct);

        Assert.Contains(outcome.ProfileErrors, e => e.Path == "setup.account");
        Assert.Empty(_mounts.Mounted);
    }

    [Fact]
    public async Task Changed_hash_is_refused_without_mounting()
    {
        var outcome = await Runner().PrepareAsync(Iso, WriteProfile(), "reviewed-hash", Machine, Exe, Ct);

        Assert.True(outcome.ProfileChanged);
        Assert.Null(outcome.Media);
        Assert.Empty(_mounts.Mounted);
    }

    [Fact]
    public async Task Matching_hash_mounts()
    {
        var path = WriteProfile();
        var hash = new ProfileService([_module]).Load(path).Profile!.Hash;

        var outcome = await Runner().PrepareAsync(Iso, path, hash, Machine, Exe, Ct);

        Assert.False(outcome.ProfileChanged);
        Assert.NotNull(outcome.Media);
        await outcome.Media.DisposeAsync();
    }

    [Fact]
    public async Task Missing_input_is_reported_without_mounting()
    {
        _module.Inputs.Add(new ExternalInput("modules.fake.file", Path.Combine(_dir, "gone.txt"), ExternalInputKind.File));

        var outcome = await Runner().PrepareAsync(Iso, WriteProfile(), null, Machine, Exe, Ct);

        Assert.Equal(["modules.fake.file"], outcome.MissingInputs);
        Assert.Empty(_mounts.Mounted);
    }

    [Fact]
    public async Task Invalid_image_returns_the_message_and_dismounts()
    {
        _images.Images[@"E:\sources\install.wim"] = [];

        var outcome = await Runner().PrepareAsync(Iso, null, null, Machine, Exe, Ct);

        Assert.Null(outcome.Media);
        Assert.NotNull(outcome.Error);
        Assert.Equal([Iso], _mounts.Dismounted);
    }

    [Fact]
    public async Task Sources_hold_the_iso_the_exe_the_profile_and_the_extra_paths()
    {
        var path = WriteProfile();
        var outcome = await Runner().PrepareAsync(Iso, path, null, Machine, Exe, Ct);

        var sources = MediaRunner.SourcePaths(outcome.Request!, path, @"C:\ui\FormatKitUI.exe");

        Assert.Equal([Iso, Exe, path, @"C:\ui\FormatKitUI.exe"], sources);
        await outcome.Media!.DisposeAsync();
    }
}
