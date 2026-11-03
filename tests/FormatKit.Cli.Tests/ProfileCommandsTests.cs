using System.Globalization;
using System.Text.Json.Nodes;
using FormatKit.Cli.Commands;
using FormatKit.Cli.Tests.Fakes;
using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Core.Running;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Dev;
using FormatKit.Drivers.Catalog;
using FormatKit.Prepare;

namespace FormatKit.Cli.Tests;

[Collection("Culture")]
public sealed class ProfileCommandsTests : IDisposable
{
    private const string Output = @"E:\p.json";

    private readonly CultureInfo _original = CultureInfo.CurrentUICulture;
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeProcessRunner _processes = new();
    private readonly FakeElevation _elevation = new() { IsElevated = false };
    private readonly FakeModule _programs = new("programs");
    private readonly StringWriter _out = new();

    public ProfileCommandsTests()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        PrepareCommands.Environment = () => new PrepareEnvironment(
            @"C:\Users\joao", @"C:\Users\joao\AppData\Local", @"C:\Users\joao\AppData\Roaming", 'C', "PC-TEST", s => s);
        _fs.CreateDirectory(@"E:\");
        _fs.CreateDirectory(@"C:\Users\joao");
        _processes.ThrowNotFound("winget.exe");
        _processes.ThrowNotFound("wsl.exe");
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _original;
        PrepareCommands.Environment = PrepareEnvironment.Current;
    }

    private Task<int> Run(params string[] args)
    {
        var services = TestServices.Create(processes: _processes, files: _fs, volumes: new FakeVolumes(new VolumeInfo('C', 0, BitLockerState.Off, null, null)));
        var dev = new DevEnvironment(@"C:\Users\joao", @"C:\Users\joao\Documents", "joao", "PC-TEST", 8, s => s);
        var env = new CliEnvironment(
            services, _elevation, new FakeScheduledTasks(), new FakeRebooter(), new FakeReportWriter(),
            new RunStateStore(_fs, new AppPaths("data")),
            Serilog.Core.Logger.None, _out, TimeSpan.Zero, TimeSpan.Zero, Dev: dev);
        return new CliApp(env, VendorCatalog.LoadEmbedded(), [new FakeModule("drivers"), _programs, new FakeModule("cleanup"), new FakeModule("dev")])
            .RunAsync(["profile", "capture", .. args], CancellationToken.None);
    }

    [Fact]
    public async Task Writes_a_profile_and_exits_0()
    {
        Assert.Equal(ExitCodes.Ok, await Run(Output));

        var root = JsonNode.Parse(_fs.ReadAllText(Output))!;
        Assert.Equal("PC-TEST", root["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Does_not_ask_for_elevation()
    {
        Assert.Equal(ExitCodes.Ok, await Run(Output));

        Assert.False(_elevation.IsElevated);
        Assert.Equal(0, _elevation.RelaunchCalls);
    }

    [Fact]
    public async Task Existing_file_without_force_exits_2_and_is_untouched()
    {
        _fs.Seed(Output, "mine");

        Assert.Equal(ExitCodes.InvalidInput, await Run(Output));

        Assert.Equal("mine", _fs.ReadAllText(Output));
        Assert.Empty(_processes.Calls);
    }

    [Fact]
    public async Task Force_overwrites()
    {
        _fs.Seed(Output, "mine");

        Assert.Equal(ExitCodes.Ok, await Run(Output, "--force"));

        Assert.NotEqual("mine", _fs.ReadAllText(Output));
    }

    [Fact]
    public async Task Bad_path_exits_2() => Assert.Equal(ExitCodes.InvalidInput, await Run("bad\0path.json"));

    [Fact]
    public async Task Missing_folder_exits_1_with_a_message()
    {
        Assert.Equal(ExitCodes.CompletedWithFailures, await Run(@"E:\none\p.json"));

        Assert.Contains("could not be written", _out.ToString());
        Assert.DoesNotContain("   at ", _out.ToString());
    }

    [Fact]
    public async Task Prints_captured_not_captured_and_review_in_that_order()
    {
        await Run(Output);

        var text = _out.ToString();
        var captured = text.IndexOf("Captured:", StringComparison.Ordinal);
        var notCaptured = text.IndexOf("Not captured:", StringComparison.Ordinal);
        var review = text.IndexOf("Review:", StringComparison.Ordinal);
        Assert.True(captured >= 0 && captured < notCaptured && notCaptured < review, text);
        Assert.Contains("modules.dev.dotfiles", text);
    }

    [Fact]
    public async Task Warns_about_personal_data_but_not_the_windows_drive_elsewhere()
    {
        await Run(Output);

        Assert.Contains("personal data", _out.ToString());
        Assert.DoesNotContain("erases", _out.ToString());
    }

    [Fact]
    public async Task Warns_on_the_windows_drive()
    {
        await Run(@"C:\Users\joao\p.json");

        Assert.Contains("erases", _out.ToString());
    }

    [Fact]
    public async Task Validation_errors_exit_1_and_write_nothing()
    {
        _programs.Errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, "modules.programs", "broken"));

        Assert.Equal(ExitCodes.CompletedWithFailures, await Run(Output));

        Assert.False(_fs.FileExists(Output));
        Assert.Contains("broken", _out.ToString());
    }

    [Fact]
    public async Task Cancel_exits_130_and_writes_nothing()
    {
        _processes.OnRun = _ => throw new OperationCanceledException();

        Assert.Equal(ExitCodes.Cancelled, await Run(Output));

        Assert.False(_fs.FileExists(Output));
    }
}
