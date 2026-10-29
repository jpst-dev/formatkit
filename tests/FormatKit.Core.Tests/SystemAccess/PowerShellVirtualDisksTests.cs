using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class PowerShellVirtualDisksTests
{
    private const string Vhdx = @"D:\Dev Disks\dev.vhdx";
    private static readonly AppPaths Paths = new(@"C:\FormatKitTest");

    private static ProcessResult Result(int exit, string stdout = "", string stderr = "") =>
        new(exit, stdout, stderr, TimeSpan.Zero, false);

    private static (PowerShellVirtualDisks Disks, FakeProcessRunner Runner, InMemoryFileSystem Files) Create()
    {
        var runner = new FakeProcessRunner();
        var files = new InMemoryFileSystem();
        return (new PowerShellVirtualDisks(runner, files, Paths), runner, files);
    }

    [Fact]
    public async Task Create_writes_expected_diskpart_script()
    {
        var (disks, runner, files) = Create();
        string? script = null;
        runner.OnRun = spec =>
        {
            if (spec.FileName == "diskpart.exe")
            {
                script = files.ReadAllText(spec.Arguments[1]);
                files.Seed(Vhdx, "vhdx");
            }
        };
        runner.Enqueue("diskpart.exe", Result(0));
        runner.Enqueue("powershell.exe", Result(0));

        await disks.CreateDevDriveAsync(Vhdx, 50, 'D', "Dev", TestContext.Current.CancellationToken);

        Assert.Equal(
            ["create vdisk file=\"D:\\Dev Disks\\dev.vhdx\" maximum=51200 type=expandable",
             "select vdisk file=\"D:\\Dev Disks\\dev.vhdx\"",
             "attach vdisk",
             "convert gpt",
             "create partition primary",
             "assign letter=D"],
            script!.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(["/s", Paths.DevDriveScriptFile], runner.Calls[0].Arguments);
        var format = runner.Calls[1].Arguments[3];
        Assert.Contains("Format-Volume -DriveLetter D -DevDrive -NewFileSystemLabel 'Dev' -Confirm:$false", format);
    }

    [Fact]
    public async Task Create_makes_the_vhdx_folder_before_diskpart()
    {
        var (disks, runner, files) = Create();
        var folderExisted = false;
        runner.OnRun = spec =>
        {
            if (spec.FileName == "diskpart.exe")
            {
                folderExisted = files.DirectoryExists(@"D:\Dev Disks");
                files.Seed(Vhdx, "vhdx");
            }
        };
        runner.Enqueue("diskpart.exe", Result(0));
        runner.Enqueue("powershell.exe", Result(0));

        await disks.CreateDevDriveAsync(Vhdx, 50, 'D', "Dev", TestContext.Current.CancellationToken);

        Assert.True(folderExisted);
    }

    [Fact]
    public async Task Create_failure_detaches_and_deletes()
    {
        var (disks, runner, files) = Create();
        runner.OnRun = spec =>
        {
            if (spec.FileName == "diskpart.exe")
            {
                files.Seed(Vhdx, "vhdx");
            }
        };
        runner.Enqueue("diskpart.exe", Result(0));
        runner.Enqueue("powershell.exe", Result(1, stderr: "format failed"));
        runner.Enqueue("powershell.exe", Result(0));

        await Assert.ThrowsAsync<VirtualDiskException>(
            () => disks.CreateDevDriveAsync(Vhdx, 50, 'D', "Dev", TestContext.Current.CancellationToken));

        Assert.Contains("Dismount-DiskImage", runner.Calls[^1].Arguments[3]);
        Assert.False(files.FileExists(Vhdx));
    }

    [Fact]
    public async Task Create_failure_keeps_preexisting_vhdx()
    {
        var (disks, runner, files) = Create();
        files.Seed(Vhdx, "user data");
        runner.Enqueue("diskpart.exe", Result(1, stdout: "file exists"));

        await Assert.ThrowsAsync<VirtualDiskException>(
            () => disks.CreateDevDriveAsync(Vhdx, 50, 'D', "Dev", TestContext.Current.CancellationToken));

        Assert.True(files.FileExists(Vhdx));
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Cleanup_failure_still_reports_original_error()
    {
        var (disks, runner, files) = Create();
        runner.OnRun = spec =>
        {
            if (spec.FileName == "diskpart.exe")
            {
                files.Seed(Vhdx, "vhdx");
            }
        };
        runner.Enqueue("diskpart.exe", Result(0));
        runner.Enqueue("powershell.exe", Result(1, stderr: "format failed"));
        runner.Enqueue("powershell.exe", Result(1, stderr: "dismount failed"));

        var ex = await Assert.ThrowsAsync<VirtualDiskException>(
            () => disks.CreateDevDriveAsync(Vhdx, 50, 'D', "Dev", TestContext.Current.CancellationToken));

        Assert.Contains("format failed", ex.Message);
        Assert.Contains("dismount failed", ex.Message);
        Assert.False(files.FileExists(Paths.DevDriveScriptFile));
    }

    [Fact]
    public async Task Script_is_written_in_oem_encoding()
    {
        var (disks, runner, files) = Create();
        const string path = @"C:\Users\João\dev.vhdx";
        runner.Enqueue("diskpart.exe", Result(1));

        await Assert.ThrowsAsync<VirtualDiskException>(
            () => disks.CreateDevDriveAsync(path, 50, 'D', "Dev", TestContext.Current.CancellationToken));

        var nl = Environment.NewLine;
        var expected = ProcessOutputDecoder.OemEncoding.GetBytes(
            $"create vdisk file=\"{path}\" maximum=51200 type=expandable{nl}select vdisk file=\"{path}\"{nl}" +
            $"attach vdisk{nl}convert gpt{nl}create partition primary{nl}assign letter=D{nl}");
        Assert.Equal(expected, files.RawBytes[Paths.DevDriveScriptFile]);
    }

    [Fact]
    public async Task Script_file_deleted_after_run()
    {
        var (disks, runner, files) = Create();
        runner.Enqueue("diskpart.exe", Result(1, stdout: "error"));

        await Assert.ThrowsAsync<VirtualDiskException>(
            () => disks.CreateDevDriveAsync(Vhdx, 50, 'D', "Dev", TestContext.Current.CancellationToken));

        Assert.False(files.FileExists(Paths.DevDriveScriptFile));
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Get_parses_attached_state()
    {
        var (disks, runner, files) = Create();
        files.Seed(Vhdx, "vhdx");
        runner.Enqueue("powershell.exe", Result(0, "{\"Attached\":true,\"Letter\":\"D\",\"Label\":\"Dev\"}\r\n"));

        var state = await disks.GetAsync(Vhdx, TestContext.Current.CancellationToken);

        Assert.Equal(new VirtualDiskState(true, 'D', "Dev"), state);
        Assert.EndsWith("'D:\\Dev Disks\\dev.vhdx'", runner.Calls[0].Arguments[3]);
    }

    [Fact]
    public async Task Get_returns_null_for_missing_file()
    {
        var (disks, runner, _) = Create();

        var state = await disks.GetAsync(Vhdx, TestContext.Current.CancellationToken);

        Assert.Null(state);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Register_remount_task_encodes_mount_script_and_replaces_existing()
    {
        var (disks, runner, _) = Create();
        runner.Enqueue("powershell.exe", Result(0));

        await disks.RegisterRemountTaskAsync(@"D:\it's\dev.vhdx", TestContext.Current.CancellationToken);

        var script = runner.Calls[0].Arguments[3];
        var encoded = System.Text.RegularExpressions.Regex.Match(script, "-EncodedCommand ([A-Za-z0-9+/=]+)").Groups[1].Value;
        var decoded = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
        Assert.Equal(PowerShellVirtualDisks.MountScript(@"D:\it's\dev.vhdx"), decoded);
        Assert.Contains("-TaskName 'FormatKit-DevDrive'", script);
        Assert.Contains("-AtStartup", script);
        Assert.Contains("-Force", script);
    }

    [Fact]
    public void Mount_script_retries_then_falls_back_to_diskpart()
    {
        var script = PowerShellVirtualDisks.MountScript(@"D:\it's\dev.vhdx");

        Assert.StartsWith("$p = 'D:\\it''s\\dev.vhdx'; ", script);
        Assert.Contains("for ($i = 0; $i -lt 12; $i++)", script);
        Assert.Contains("Mount-DiskImage -ImagePath $p -ErrorAction Stop", script);
        Assert.Contains("$OutputEncoding = [Console]::OutputEncoding", script);
        Assert.Contains("\"select vdisk file=`\"$p`\"\", 'attach vdisk' | diskpart.exe", script);
    }

    [Fact]
    public async Task Attach_runs_mount_script()
    {
        var (disks, runner, _) = Create();
        runner.Enqueue("powershell.exe", Result(0));

        await disks.AttachAsync(Vhdx, TestContext.Current.CancellationToken);

        Assert.Equal(PowerShellVirtualDisks.MountScript(Vhdx), runner.Calls[0].Arguments[3]);
    }

    [Theory]
    [InlineData(@"C:\Users\Jo\u00e3o\u2019s\dev.vhdx", @"'C:\Users\Jo\u00e3o\u2019\u2019s\dev.vhdx'")]
    [InlineData(@"x\u2019; Remove-Item C:\ -Recurse; \u2019", @"'x\u2019\u2019; Remove-Item C:\ -Recurse; \u2019\u2019'")]
    [InlineData(@"a\u2018b\u201Ac\u201Bd'e", @"'a\u2018\u2018b\u201A\u201Ac\u201B\u201Bd''e'")]
    public void MountScript_doubles_every_single_quote_character(string path, string expectedLiteral)
    {
        Assert.StartsWith($"$p = {Unescape(expectedLiteral)}; ", PowerShellVirtualDisks.MountScript(Unescape(path)));
    }

    [Fact]
    public async Task Get_quotes_a_path_with_typographic_quotes()
    {
        var (disks, runner, files) = Create();
        var path = Unescape(@"x\u2019; Remove-Item C:\ -Recurse; \u2019");
        files.Seed(path, "vhdx");
        runner.Enqueue("powershell.exe", Result(0, stdout: """{"Attached":false,"Letter":null,"Label":null}"""));

        await disks.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.EndsWith(Unescape(@" 'x\u2019\u2019; Remove-Item C:\ -Recurse; \u2019\u2019'"), runner.Calls[0].Arguments[3]);
    }

    private static string Unescape(string value) =>
        System.Text.RegularExpressions.Regex.Replace(
            value, @"\\u([0-9A-Fa-f]{4})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
}
