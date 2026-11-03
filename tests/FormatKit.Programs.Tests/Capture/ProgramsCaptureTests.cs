using System.Text.Json.Nodes;
using FormatKit.Core.Capture;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Programs.Capture;
using Serilog.Core;

namespace FormatKit.Programs.Tests.Capture;

public class ProgramsCaptureTests
{
    private const string Temp = @"C:\Temp";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Export(params (string Source, string Id)[] packages)
    {
        var sources = packages.GroupBy(p => p.Source).Select(g =>
            "{\"SourceDetails\":{\"Name\":\"" + g.Key + "\"},\"Packages\":["
            + string.Join(",", g.Select(p => "{\"PackageIdentifier\":\"" + p.Id + "\"}")) + "]}");
        return "{\"Sources\":[" + string.Join(",", sources) + "]}";
    }

    /// <summary>A winget that writes <paramref name="json"/> to the file it is given.</summary>
    private static (FakeProcessRunner Runner, InMemoryFileSystem Files) Winget(string? json, int exit = 0)
    {
        var runner = new FakeProcessRunner();
        var files = new InMemoryFileSystem();
        runner.OnRun = spec =>
        {
            if (json is not null)
                files.Seed(spec.Arguments[2], json);
        };
        runner.Enqueue("winget.exe", new ProcessResult(exit, "", "", TimeSpan.Zero, false));
        return (runner, files);
    }

    private static Task<CaptureSection> Run(FakeProcessRunner runner, InMemoryFileSystem files) =>
        ProgramsCapture.RunAsync(TestServices.Create(processes: runner, files: files), ProgramsTestData.TestCatalog(), Temp, Logger.None, Ct);

    [Fact]
    public async Task Maps_catalog_programs_and_keeps_the_rest_as_winget()
    {
        var (runner, files) = Winget(Export(("winget", "mozilla.firefox"), ("winget", "Vendor.Tool"), ("msstore", "9NKSQGP7F2NH")));

        var section = await Run(runner, files);

        Assert.True(section.Value["enabled"]!.GetValue<bool>());
        Assert.Equal(["browsers.firefox"], section.Value["catalog"]!.AsArray().Select(n => n!.GetValue<string>()));
        var winget = section.Value["winget"]!.AsArray().Select(n => n!.AsObject()).ToList();
        Assert.Equal(2, winget.Count);
        Assert.Equal("Vendor.Tool", winget[0]["id"]!.GetValue<string>());
        Assert.Equal("winget", winget[0]["source"]!.GetValue<string>());
        Assert.Equal(2, winget[0].Count);
        Assert.Equal("msstore", winget[1]["source"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_package_from_another_source_than_the_catalog_stays_winget()
    {
        var (runner, files) = Winget(Export(("msstore", "Mozilla.Firefox")));

        var section = await Run(runner, files);

        Assert.Null(section.Value["catalog"]);
        Assert.Equal("Mozilla.Firefox", section.Value["winget"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_repeated_package_is_captured_once()
    {
        var (runner, files) = Winget(Export(("winget", "Vendor.Tool"), ("winget", "vendor.tool"), ("winget", "Mozilla.Firefox"), ("winget", "Mozilla.Firefox")));

        var section = await Run(runner, files);

        Assert.Single(section.Value["winget"]!.AsArray());
        Assert.Single(section.Value["catalog"]!.AsArray());
    }

    [Fact]
    public async Task Never_accepts_agreements()
    {
        var (runner, files) = Winget(Export(("winget", "Vendor.Tool")));

        var section = await Run(runner, files);

        Assert.False(section.Value["acceptAgreements"]!.GetValue<bool>());
        Assert.DoesNotContain("--accept-source-agreements", runner.Calls[0].Arguments);
        Assert.DoesNotContain("--accept-package-agreements", runner.Calls[0].Arguments);
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.Review && n.Path == "modules.programs.acceptAgreements");
    }

    [Theory]
    [InlineData("Microsoft.VCRedist.2015+.x64")]
    [InlineData("Microsoft.VCLibs.Desktop.14")]
    [InlineData("Microsoft.UI.Xaml.2.8")]
    [InlineData("Microsoft.WindowsAppRuntime.1.5")]
    [InlineData("Microsoft.DotNet.Runtime.8")]
    [InlineData("Microsoft.DotNet.DesktopRuntime.8")]
    [InlineData("Microsoft.DotNet.AspNetCore.8")]
    [InlineData("Microsoft.Edge")]
    [InlineData("Microsoft.EdgeWebView2Runtime")]
    [InlineData("Microsoft.AppInstaller")]
    [InlineData("microsoft.vcredist.2015+.x64")]
    public void Skips_runtime_packages(string id) => Assert.True(SkippedPackages.IsSkipped(id));

    [Theory]
    [InlineData("Microsoft.VisualStudioCode")]
    [InlineData("Microsoft.DotNet.SDK.8")]
    [InlineData("Microsoft.PowerShell")]
    [InlineData("Microsoft.EdgeDev")]
    [InlineData("Vendor.Tool")]
    public void Keeps_other_packages(string id) => Assert.False(SkippedPackages.IsSkipped(id));

    [Fact]
    public async Task Skipped_packages_are_left_out_and_counted()
    {
        var (runner, files) = Winget(Export(("winget", "Microsoft.VCRedist.2015+.x64"), ("winget", "Microsoft.Edge"), ("winget", "Vendor.Tool")));

        var section = await Run(runner, files);

        Assert.Single(section.Value["winget"]!.AsArray());
        Assert.Contains("2", section.Summary);
    }

    [Fact]
    public async Task Winget_missing_disables_the_section_with_a_note()
    {
        var runner = new FakeProcessRunner();
        var files = new InMemoryFileSystem();
        runner.OnRun = _ => throw new FileNotFoundException("winget.exe");

        var section = await Run(runner, files);

        Assert.False(section.Value["enabled"]!.GetValue<bool>());
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.NotCaptured && n.Path == "modules.programs");
    }

    [Fact]
    public async Task Export_failure_disables_the_section_and_explains_the_agreements()
    {
        var (runner, files) = Winget(null, exit: 1);

        var section = await Run(runner, files);

        Assert.False(section.Value["enabled"]!.GetValue<bool>());
        var note = Assert.Single(section.Notes);
        Assert.Equal(CaptureNoteKind.NotCaptured, note.Kind);
        Assert.Contains("winget list", note.Text);
    }

    [Fact]
    public async Task Unreadable_export_disables_the_section_with_a_note()
    {
        var (runner, files) = Winget("not json");

        var section = await Run(runner, files);

        Assert.False(section.Value["enabled"]!.GetValue<bool>());
        Assert.Contains(section.Notes, n => n.Kind == CaptureNoteKind.NotCaptured);
    }

    [Fact]
    public async Task Empty_export_disables_the_section()
    {
        var (runner, files) = Winget(Export());

        var section = await Run(runner, files);

        Assert.False(section.Value["enabled"]!.GetValue<bool>());
        Assert.Null(section.Value["acceptAgreements"]);
        Assert.DoesNotContain(section.Notes, n => n.Kind == CaptureNoteKind.Review);
    }

    [Fact]
    public async Task Temp_file_is_deleted_on_success()
    {
        var (runner, files) = Winget(Export(("winget", "Vendor.Tool")));

        await Run(runner, files);

        Assert.StartsWith(Temp, runner.Calls[0].Arguments[2]);
        Assert.False(files.FileExists(runner.Calls[0].Arguments[2]));
    }

    [Fact]
    public async Task Temp_file_is_deleted_on_failure()
    {
        var (runner, files) = Winget("partial", exit: 1);

        await Run(runner, files);

        Assert.False(files.FileExists(runner.Calls[0].Arguments[2]));
    }

    [Fact]
    public async Task Temp_file_is_deleted_on_cancel()
    {
        var runner = new FakeProcessRunner();
        var files = new InMemoryFileSystem();
        string? path = null;
        runner.OnRun = spec =>
        {
            path = spec.Arguments[2];
            files.Seed(path, "partial");
            throw new OperationCanceledException();
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => Run(runner, files));

        Assert.False(files.FileExists(path!));
    }
}
