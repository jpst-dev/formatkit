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
    public async Task Register_remount_task_doubles_single_quotes_and_replaces_existing()
    {
        var (disks, runner, _) = Create();
        runner.Enqueue("powershell.exe", Result(0));

        await disks.RegisterRemountTaskAsync(@"D:\it's\dev.vhdx", TestContext.Current.CancellationToken);

        var script = runner.Calls[0].Arguments[3];
        Assert.Contains("Mount-DiskImage -ImagePath ''D:\\it''''s\\dev.vhdx''", script);
        Assert.Contains("-TaskName 'FormatKit-DevDrive'", script);
        Assert.Contains("-AtStartup", script);
        Assert.Contains("-Force", script);
    }
}
