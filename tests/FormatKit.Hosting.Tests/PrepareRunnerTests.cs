using System.Text.Json;
using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare;

namespace FormatKit.Hosting.Tests;

public sealed class PrepareRunnerTests
{
    private sealed class CapturingCheck : IPrepareCheck
    {
        public string Id => "fake.check";
        public PrepareGroup Group => PrepareGroup.Data;
        public PrepareContext? Context { get; private set; }

        public Task<IReadOnlyList<PrepareItem>> RunAsync(PrepareContext context, CancellationToken ct)
        {
            Context = context;
            return Task.FromResult<IReadOnlyList<PrepareItem>>([]);
        }
    }

    private readonly InMemoryFileSystem _files = new();
    private readonly FakeVolumes _volumes = new(new VolumeInfo('C', 0, BitLockerState.Off, null, null));
    private readonly CapturingCheck _check = new();

    private static readonly LoadedProfile Profile =
        new(new Profile(1, "p", new Dictionary<string, JsonElement>()), @"C:\p\profile.json", "hash1");

    private PrepareRunner Create(params IModule[] modules)
    {
        var env = new HostEnvironment(
            TestServices.Create(files: _files, volumes: _volumes),
            new Elevation(),
            new FakeScheduledTasks(),
            new FakeRebooter(),
            new FakeReportWriter(),
            new RunStateStore(_files, new AppPaths(@"C:\FormatKitTest")),
            new Serilog.LoggerConfiguration().CreateLogger(),
            TimeSpan.Zero,
            TimeSpan.Zero);
        return new PrepareRunner(
            env,
            modules,
            () => [_check],
            () => new PrepareEnvironment(@"C:\Users\joao", @"C:\Users\joao\AppData\Local", @"C:\Users\joao\AppData\Roaming", 'C', "PC-TEST", s => s));
    }

    [Fact]
    public async Task Run_WithProfile_IncludesProfileFileAsInput()
    {
        var run = await Create().RunAsync(Profile, TestContext.Current.CancellationToken);

        var input = Assert.Single(_check.Context!.Inputs);
        Assert.Equal(@"C:\p\profile.json", input.ResolvedPath);
        Assert.Equal(ExternalInputKind.File, input.Kind);
        Assert.Equal("PC-TEST", run.ComputerName);
    }

    [Fact]
    public async Task Run_VolumeListingFails_StillRuns()
    {
        _volumes.Throw = new InvalidOperationException("no volumes");

        var run = await Create().RunAsync(null, TestContext.Current.CancellationToken);

        Assert.NotNull(run.Report);
        Assert.Empty(_check.Context!.Inputs);
        Assert.False(run.Windows.IsLost(@"D:\x"));
    }

    [Fact]
    public async Task WriteMarkdown_OnSystemDrive_FlagsIt()
    {
        var runner = Create();
        var run = await runner.RunAsync(null, TestContext.Current.CancellationToken);

        _files.CreateDirectory(@"C:\out");
        var write = runner.WriteMarkdown(run, @"C:\out\check.md");

        Assert.True(write.OnWindowsDrive);
        Assert.Null(write.Error);
        Assert.True(_files.FileExists(@"C:\out\check.md"));
    }

    [Fact]
    public async Task WriteMarkdown_WriteFails_ReturnsError()
    {
        var runner = Create();
        var run = await runner.RunAsync(null, TestContext.Current.CancellationToken);
        _files.AtomicWriteError = new IOException("disk full");

        var write = runner.WriteMarkdown(run, @"D:\out\check.md");

        Assert.False(write.OnWindowsDrive);
        Assert.Equal("disk full", write.Error);
    }
}
