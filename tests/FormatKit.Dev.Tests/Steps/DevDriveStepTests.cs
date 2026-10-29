using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Dev.Localization;
using FormatKit.Dev.Steps;

namespace FormatKit.Dev.Tests.Steps;

public class DevDriveStepTests
{
    private const string Vhdx = @"C:\DevDrive\dev.vhdx";
    private const string VersionKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    private sealed class Rig
    {
        public FakeVirtualDisks Disks { get; } = new();
        public FakeRegistry Registry { get; } = new();
        public FakeUserEnvironment Environment { get; } = new();
        public InMemoryFileSystem Files { get; } = new();

        public Rig(string build = "22631", uint ubr = 2338)
        {
            Registry.Set(VersionKey, "CurrentBuildNumber", RegistryKind.String, build);
            Registry.Set(VersionKey, "UBR", RegistryKind.Dword, ubr.ToString());
        }

        public Task<StepResult> Run(DevDriveOptions options)
        {
            var ctx = TestServices.Context(TestServices.Create(
                files: Files, registry: Registry, userEnvironment: Environment, virtualDisks: Disks));
            return new DevDriveStep(options).ExecuteAsync(ctx, TestContext.Current.CancellationToken);
        }
    }

    private static DevDriveOptions Options(params string[] caches) => new(Vhdx, 100, 'D', "Dev", caches);

    private static string Message(StepResult result, string label) =>
        Assert.Single(result.Messages, m => m.StartsWith(label + ":", StringComparison.Ordinal));

    private static string Text(string outcome) => Strings.Get($"Outcome_{outcome}");

    [Fact]
    public void Identity()
    {
        var step = new DevDriveStep(Options());

        Assert.Equal("dev.devdrive", step.Id);
        Assert.True(step.RequiresAdmin);
    }

    [Theory]
    [InlineData("22000", 5000u)]
    [InlineData("22621", 2337u)]
    [InlineData("", 9999u)]
    public async Task Old_build_not_applicable(string build, uint ubr)
    {
        var rig = new Rig(build, ubr);

        var result = await rig.Run(Options("npm"));

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Empty(rig.Disks.Calls);
        Assert.Empty(rig.Environment.Values);
        Assert.All(result.Messages, m => Assert.Contains(Text("NotApplicable"), m));
        Assert.Equal(3, result.Messages.Count); // drive, remount, npm
    }

    [Fact]
    public async Task Unreadable_build_counts_as_old()
    {
        var rig = new Rig();
        rig.Registry.Delete(VersionKey, "UBR");

        var result = await rig.Run(Options());

        Assert.Empty(rig.Disks.Calls);
    }

    [Fact]
    public async Task Newer_major_build_with_low_ubr_is_fine()
    {
        var rig = new Rig("26100", 1);

        await rig.Run(Options());

        Assert.Contains(rig.Disks.Calls, c => c.StartsWith("Create ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Creates_drive_and_sets_caches()
    {
        var rig = new Rig();

        var result = await rig.Run(Options("npm", "nuget", "pip", "cargo"));

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Contains($"Create {Vhdx} 100 D Dev", rig.Disks.Calls);
        Assert.Contains($"RegisterRemount {Vhdx}", rig.Disks.Calls);
        Assert.Contains(Text("Done"), Message(result, "Dev Drive"));
        Assert.Contains(Text("Done"), Message(result, "remount at startup"));
        Assert.Equal(@"D:\packages\npm", rig.Environment.Get("npm_config_cache"));
        Assert.Equal(@"D:\packages\nuget", rig.Environment.Get("NUGET_PACKAGES"));
        Assert.Equal(@"D:\packages\pip", rig.Environment.Get("PIP_CACHE_DIR"));
        Assert.Equal(@"D:\packages\cargo", rig.Environment.Get("CARGO_HOME"));
        Assert.True(rig.Files.DirectoryExists(@"D:\packages\npm"));
        Assert.True(rig.Files.DirectoryExists(@"D:\packages\cargo"));
    }

    [Fact]
    public async Task Second_run_already_in_order()
    {
        var rig = new Rig();
        await rig.Run(Options("npm"));
        rig.Disks.Calls.Clear();

        var result = await rig.Run(Options("npm"));

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.DoesNotContain(rig.Disks.Calls, c => c.StartsWith("Create ", StringComparison.Ordinal) || c.StartsWith("Attach ", StringComparison.Ordinal));
        Assert.Contains(Text("AlreadyInOrder"), Message(result, "Dev Drive"));
        Assert.Contains(Text("AlreadyInOrder"), Message(result, "npm"));
        // There is no way to tell whether the task exists, so it is registered (idempotent) on every run.
        Assert.Contains($"RegisterRemount {Vhdx}", rig.Disks.Calls);
    }

    [Fact]
    public async Task Detached_vhdx_attached_again()
    {
        var rig = new Rig();
        // The fake keeps the letter and label on attach, as the real drive shows them once attached.
        rig.Disks.States[Vhdx] = new VirtualDiskState(false, 'D', "Dev");

        var result = await rig.Run(Options("npm"));

        Assert.Contains($"Attach {Vhdx}", rig.Disks.Calls);
        Assert.DoesNotContain(rig.Disks.Calls, c => c.StartsWith("Create ", StringComparison.Ordinal));
        Assert.Contains(Text("Done"), Message(result, "Dev Drive"));
        Assert.Equal(@"D:\packages\npm", rig.Environment.Get("npm_config_cache"));
    }

    [Fact]
    public async Task Attached_with_other_label_fails()
    {
        var rig = new Rig();
        rig.Disks.States[Vhdx] = new VirtualDiskState(true, 'D', "Other");

        var result = await rig.Run(Options("npm"));

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains(Strings.Get("Next_OtherVhdx"), result.NextAction);
        Assert.Contains(Text("NotApplicable"), Message(result, "npm"));
        Assert.Null(rig.Environment.Get("npm_config_cache"));
    }

    [Fact]
    public async Task Letter_in_use_fails_before_creating()
    {
        var rig = new Rig();
        rig.Disks.LettersInUse.Add('D');

        var result = await rig.Run(Options("npm"));

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.DoesNotContain(rig.Disks.Calls, c => c.StartsWith("Create ", StringComparison.Ordinal));
        Assert.Contains("D:", Message(result, "Dev Drive"));
        Assert.Contains(Text("NotApplicable"), Message(result, "npm"));
    }

    [Fact]
    public async Task Low_space_fails_before_creating()
    {
        var rig = new Rig();
        rig.Disks.FreeBytesValue = 109L * 1024 * 1024 * 1024;

        var result = await rig.Run(Options("npm"));

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.DoesNotContain(rig.Disks.Calls, c => c.StartsWith("Create ", StringComparison.Ordinal));
        var message = Message(result, "Dev Drive");
        Assert.Contains("109", message);
        Assert.Contains("110", message);
    }

    [Fact]
    public async Task Create_failure_fails_and_skips_caches()
    {
        var rig = new Rig();
        rig.Disks.CreateFailure = new VirtualDiskException("boom");

        var result = await rig.Run(Options("npm", "pip"));

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains(Strings.Get("Next_SeeLog"), result.NextAction);
        Assert.Empty(rig.Environment.Values);
        Assert.DoesNotContain(rig.Disks.Calls, c => c.StartsWith("RegisterRemount", StringComparison.Ordinal));
        Assert.Contains(Text("NotApplicable"), Message(result, "npm"));
        Assert.Contains(Text("NotApplicable"), Message(result, "pip"));
        Assert.Contains(Text("NotApplicable"), Message(result, "remount at startup"));
    }

    [Fact]
    public async Task Remount_failure_fails_only_that_item()
    {
        var rig = new Rig();
        rig.Disks.RemountFailure = new VirtualDiskException("no task");

        var result = await rig.Run(Options("npm"));

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains(Text("Failed"), Message(result, "remount at startup"));
        Assert.Contains(Text("Done"), Message(result, "Dev Drive"));
        Assert.Equal(@"D:\packages\npm", rig.Environment.Get("npm_config_cache"));
    }

    [Fact]
    public async Task Free_space_lookup_failure_keeps_all_items()
    {
        var rig = new Rig();
        rig.Disks.FreeBytesFailure = new DriveNotFoundException("no drive");

        var result = await rig.Run(Options("npm"));

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains(Strings.Get("Next_SeeLog"), result.NextAction);
        Assert.Contains("no drive", Message(result, "Dev Drive"));
        Assert.Contains(Text("NotApplicable"), Message(result, "remount at startup"));
        Assert.Contains(Text("NotApplicable"), Message(result, "npm"));
        Assert.DoesNotContain(rig.Disks.Calls, c => c.StartsWith("Create ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Vhdx_missing_after_attach_is_not_created_over()
    {
        var rig = new Rig();
        rig.Disks.States[Vhdx] = new VirtualDiskState(false, null, null);
        rig.Disks.VanishOnAttach = true;

        var result = await rig.Run(Options("npm"));

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.DoesNotContain(rig.Disks.Calls, c => c.StartsWith("Create ", StringComparison.Ordinal));
        Assert.Contains(Text("NotApplicable"), Message(result, "npm"));
    }

    [Fact]
    public async Task Label_and_letter_compare_ignoring_case()
    {
        var rig = new Rig();
        rig.Disks.States[Vhdx] = new VirtualDiskState(true, 'd', "DEV");

        var result = await rig.Run(Options());

        Assert.Contains(Text("AlreadyInOrder"), Message(result, "Dev Drive"));
    }

    [Fact]
    public async Task Cache_variable_elsewhere_kept()
    {
        var rig = new Rig();
        rig.Environment.Set("npm_config_cache", @"E:\mine");

        var result = await rig.Run(Options("npm"));

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(@"E:\mine", rig.Environment.Get("npm_config_cache"));
        var message = Message(result, "npm");
        Assert.Contains(Text("AlreadyInOrder"), message);
        Assert.Contains(Strings.Format("Detail_Kept", @"E:\mine"), message);
    }
}
