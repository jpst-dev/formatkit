using System.Text.Json.Nodes;
using FormatKit.Core.Capture;
using FormatKit.Core.Profiles;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Dev;
using FormatKit.Media.Setup;
using Serilog.Core;

namespace FormatKit.Hosting.Tests;

public sealed class ProfileCaptureTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DevEnvironment Dev = new(
        @"C:\Users\Joao", @"C:\Users\Joao\Documents", "Joao", "PC-JOAO", 8, s => s);

    private static CaptureSection Section(JsonObject value, params CaptureNote[] notes) => new(value, notes, "summary");

    private static CaptureSection Off() => Section(new JsonObject { ["enabled"] = false });

    private static CaptureSources Sources(
        CaptureSection? programs = null, CaptureSection? cleanup = null, CaptureSection? dev = null, CaptureSection? setup = null) => new(
        _ => Task.FromResult(programs ?? Off()),
        _ => Task.FromResult(cleanup ?? Off()),
        _ => Task.FromResult(dev ?? Off()),
        () => setup ?? Section([]));

    private static IReadOnlyList<IModule> Modules(ServiceSet? services = null) =>
        ModuleSet.CreateReal(services ?? TestServices.Create(), Logger.None, TimeSpan.Zero, Dev).Modules;

    private static ProfileCapture Create(CaptureSources sources, InMemoryFileSystem? files = null, FakeVolumes? volumes = null) =>
        new(Modules(), files ?? new InMemoryFileSystem(), volumes ?? new FakeVolumes(), sources, "PC-JOAO", 'C');

    private static JsonNode Parsed(ProfileCaptureResult result) => JsonNode.Parse(result.Json!)!;

    [Fact]
    public async Task Output_has_every_module_with_its_enabled_flag()
    {
        var result = await Create(Sources()).RunAsync(Ct);

        var modules = Parsed(result)["modules"]!.AsObject();
        Assert.Equal(["drivers", "programs", "cleanup", "dev"], modules.Select(m => m.Key));
        Assert.All(modules, m => Assert.NotNull(m.Value!["enabled"]));
    }

    private static JsonObject ShellWithPosh(bool posh) => new()
    {
        ["enabled"] = true,
        ["shell"] = new JsonObject { ["windows"] = new JsonObject { ["ohMyPosh"] = posh } },
    };

    private static JsonObject CleanupWith(params string[] include) => new()
    {
        ["enabled"] = true,
        ["include"] = new JsonArray([.. include.Select(i => (JsonNode)JsonValue.Create(i)!)]),
    };

    [Fact]
    public async Task Oh_My_Posh_without_the_execution_policy_item_asks_for_review()
    {
        var result = await Create(Sources(cleanup: Section(CleanupWith("apps.solitaire")), dev: Section(ShellWithPosh(true)))).RunAsync(Ct);

        var note = Assert.Single(result.Notes, n => n.Path == "modules.cleanup.include");
        Assert.Equal(CaptureNoteKind.Review, note.Kind);
        Assert.Contains("developer.ps-execution-policy", note.Text);
    }

    [Fact]
    public async Task Oh_My_Posh_with_the_execution_policy_item_needs_no_review()
    {
        var result = await Create(Sources(cleanup: Section(CleanupWith("developer.ps-execution-policy")), dev: Section(ShellWithPosh(true)))).RunAsync(Ct);

        Assert.DoesNotContain(result.Notes, n => n.Path == "modules.cleanup.include");
    }

    [Fact]
    public async Task No_Oh_My_Posh_needs_no_execution_policy_review()
    {
        var result = await Create(Sources(cleanup: Section(CleanupWith()), dev: Section(ShellWithPosh(false)))).RunAsync(Ct);

        Assert.DoesNotContain(result.Notes, n => n.Path == "modules.cleanup.include");
    }

    [Fact]
    public async Task Name_and_schema_are_set()
    {
        var root = Parsed(await Create(Sources()).RunAsync(Ct));

        Assert.Equal("PC-JOAO", root["name"]!.GetValue<string>());
        Assert.Equal(ProfileLoader.CurrentSchemaVersion, root["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task Drivers_section_is_fixed()
    {
        var drivers = Parsed(await Create(Sources()).RunAsync(Ct))["modules"]!["drivers"]!;

        Assert.True(drivers["enabled"]!.GetValue<bool>());
        Assert.Equal(["windowsUpdate", "oem", "gpu"], drivers["sources"]!.AsArray().Select(s => s!.GetValue<string>()));
    }

    [Fact]
    public async Task Empty_machine_is_still_valid()
    {
        var result = await Create(Sources()).RunAsync(Ct);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Json);
        Assert.Empty(new ProfileLoader(Modules()).Parse(result.Json!, "x.json").Errors);
    }

    [Fact]
    public async Task Validates_with_agreements_accepted_but_keeps_false_in_the_output()
    {
        var programs = Section(new JsonObject
        {
            ["enabled"] = true,
            ["acceptAgreements"] = false,
            ["winget"] = new JsonArray(new JsonObject { ["id"] = "Vendor.Tool", ["source"] = "winget" }),
        });

        var result = await Create(Sources(programs: programs)).RunAsync(Ct);

        Assert.Empty(result.Errors);
        Assert.False(Parsed(result)["modules"]!["programs"]!["acceptAgreements"]!.GetValue<bool>());
        Assert.NotEmpty(new ProfileLoader(Modules()).Parse(result.Json!, "x.json").Errors);
    }

    [Fact]
    public async Task Invalid_assembly_returns_errors_and_no_json()
    {
        var cleanup = Section(new JsonObject { ["enabled"] = true, ["include"] = new JsonArray("no.such.item") });

        var result = await Create(Sources(cleanup: cleanup)).RunAsync(Ct);

        Assert.Null(result.Json);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task Invalid_setup_returns_errors_and_no_json()
    {
        var result = await Create(Sources(setup: Section(new JsonObject { ["computerName"] = "not valid!" }))).RunAsync(Ct);

        Assert.Null(result.Json);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task Non_ascii_is_written_literally_and_round_trips()
    {
        var dev = Section(new JsonObject
        {
            ["enabled"] = true,
            ["git"] = new JsonObject { ["name"] = "João Ação", ["email"] = "j@example.com" },
        });

        var result = await Create(Sources(dev: dev)).RunAsync(Ct);

        Assert.Contains("João Ação", result.Json);
        Assert.Equal("João Ação", Parsed(result)["modules"]!["dev"]!["git"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Notes_and_summaries_are_carried()
    {
        var note = new CaptureNote(CaptureNoteKind.Review, "modules.dev.ssh", "text");

        var result = await Create(Sources(dev: Section(new JsonObject { ["enabled"] = false }, note))).RunAsync(Ct);

        Assert.Contains(note, result.Notes);
        Assert.Equal(["drivers", "programs", "cleanup", "dev", "setup"], result.Sections.Select(s => s.Section));
    }

    [Fact]
    public async Task A_source_that_throws_costs_only_its_section()
    {
        var sources = Sources() with { Programs = _ => throw new System.ComponentModel.Win32Exception(5) };

        var result = await Create(sources).RunAsync(Ct);

        Assert.Empty(result.Errors);
        Assert.False(Parsed(result)["modules"]!["programs"]!["enabled"]!.GetValue<bool>());
        Assert.Contains(result.Notes, n => n.Kind == CaptureNoteKind.NotCaptured && n.Path == "modules.programs");
    }

    [Fact]
    public async Task A_setup_source_that_throws_leaves_setup_empty()
    {
        var sources = Sources() with { Setup = () => throw new UnauthorizedAccessException("no") };

        var result = await Create(sources).RunAsync(Ct);

        Assert.Empty(result.Errors);
        Assert.Empty(Parsed(result)["setup"]!.AsObject());
        Assert.Contains(result.Notes, n => n.Kind == CaptureNoteKind.NotCaptured && n.Path == "setup");
    }

    [Fact]
    public async Task Cancellation_passes_through()
    {
        var sources = Sources() with { Dev = _ => throw new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() => Create(sources).RunAsync(Ct));
    }

    [Fact]
    public async Task Write_stores_the_file_and_reports_the_windows_drive()
    {
        var files = new InMemoryFileSystem();
        files.CreateDirectory(@"C:\Users\Joao");
        files.CreateDirectory(@"E:\");
        var capture = Create(Sources(), files);
        var result = new ProfileCaptureResult("{}", [], [], []);

        var onWindows = await capture.WriteAsync(result, @"C:\Users\Joao\p.json", Ct);
        var elsewhere = await capture.WriteAsync(result, @"E:\p.json", Ct);

        Assert.True(onWindows.OnWindowsDrive);
        Assert.False(elsewhere.OnWindowsDrive);
        Assert.Null(onWindows.Error);
        Assert.Equal("{}", files.ReadAllText(@"E:\p.json"));
    }

    [Fact]
    public async Task Write_failure_is_reported()
    {
        var files = new InMemoryFileSystem { AtomicWriteError = new DirectoryNotFoundException("no folder") };

        var write = await Create(Sources(), files).WriteAsync(new ProfileCaptureResult("{}", [], [], []), @"E:\none\p.json", Ct);

        Assert.Contains("no folder", write.Error);
    }

    [Fact]
    public async Task Real_sources_on_a_bare_machine_give_a_valid_profile_and_change_nothing()
    {
        var processes = new FakeProcessRunner();
        processes.ThrowNotFound("winget.exe");
        processes.ThrowNotFound("wsl.exe");
        processes.OnRun = spec => Assert.DoesNotContain("--accept-source-agreements", spec.Arguments);
        var files = new InMemoryFileSystem();
        var registry = new FakeRegistry();
        var serviceControl = new FakeServiceControl();
        var services = TestServices.Create(processes: processes, files: files, registry: registry, services: serviceControl);
        var set = ModuleSet.CreateReal(services, Logger.None, TimeSpan.Zero, Dev);
        var sources = ProfileCapture.RealSources(services, set.Programs, set.Cleanup, Dev, 'C', @"C:\Temp", new Core.AppPaths(@"C:\FormatKitTest"), Logger.None);

        var result = await new ProfileCapture(set.Modules, files, services.Volumes, sources, "PC-JOAO", 'C').RunAsync(Ct);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Json);
        Assert.Empty(files.Files);
        Assert.Empty(registry.Values);
        Assert.Empty(serviceControl.Calls);
    }
}
