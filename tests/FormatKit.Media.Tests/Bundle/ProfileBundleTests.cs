using System.Text;
using System.Text.Json.Nodes;
using FormatKit.Core.Profiles;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Media.Bundle;

namespace FormatKit.Media.Tests.Bundle;

public class ProfileBundleTests
{
    private const string ProfileText = """
        {
          // a comment
          "setup": { "language": "pt-BR" },
          "modules": {
            "programs": {
              "importFile": "C:/data/programs.json",
              "direct": [ { "url": "https://x/y.exe" }, { "path": "C:/data/tool.msi", } ]
            },
            "drivers": { "networkDriverBackup": "C:/data/netdrivers" }
          }
        }
        """;

    private static (InMemoryFileSystem fs, LoadedProfile profile, List<ExternalInput> inputs) Arrange()
    {
        var fs = new InMemoryFileSystem();
        fs.Seed(@"C:\p\profile.json", ProfileText);
        fs.Seed(@"C:\data\programs.json", "0123456789");
        fs.Seed(@"C:\data\netdrivers\a.inf", "12345");
        fs.Seed(@"C:\data\netdrivers\sub\b.sys", "1234567");
        fs.Seed(@"C:\data\tool.msi", "abc");
        fs.Seed(@"C:\exe\formatkit.exe", "EXE");
        var inputs = new List<ExternalInput>
        {
            new("modules.programs.importFile", @"C:\data\programs.json", ExternalInputKind.File),
            new("modules.drivers.networkDriverBackup", @"C:\data\netdrivers", ExternalInputKind.Folder),
        };
        return (fs, new LoadedProfile(null!, @"C:\p\profile.json", "h"), inputs);
    }

    [Fact]
    public void Plan_names_items_and_counts_bytes()
    {
        var (fs, profile, inputs) = Arrange();
        var plan = ProfileBundle.Plan(profile, inputs, fs);
        Assert.Equal([@"inputs\1-programs.json", @"inputs\2-netdrivers"], plan.Items.Select(i => i.RelativePath));
        Assert.Equal([10L, 12L], plan.Items.Select(i => i.Bytes));
        Assert.Equal(22 + Encoding.UTF8.GetByteCount(ProfileText), plan.TotalBytes);
        Assert.Empty(plan.Missing);
    }

    [Fact]
    public void Plan_lists_missing_input_by_json_path()
    {
        var (fs, profile, inputs) = Arrange();
        inputs.Add(new("modules.dev.dotfiles.path", @"C:\nope\dots", ExternalInputKind.Folder));
        var plan = ProfileBundle.Plan(profile, inputs, fs);
        Assert.Equal(["modules.dev.dotfiles.path"], plan.Missing);
        Assert.Equal(2, plan.Items.Count);
    }

    [Fact]
    public void Same_resolved_path_is_copied_once_and_counted_once()
    {
        var (fs, profile, inputs) = Arrange();
        inputs.Add(new("modules.programs.direct[1].path", @"C:\DATA\Programs.json", ExternalInputKind.File));
        var plan = ProfileBundle.Plan(profile, inputs, fs);
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(22 + Encoding.UTF8.GetByteCount(ProfileText), plan.TotalBytes);

        plan.Write(@"E:\FormatKit", @"C:\exe\formatkit.exe", fs, null, CancellationToken.None);
        var json = JsonNode.Parse(fs.ReadAllText(@"E:\FormatKit\profile.json"))!;
        Assert.Equal(@"inputs\1-programs.json", (string?)json["modules"]!["programs"]!["importFile"]);
        Assert.Equal(@"inputs\1-programs.json", (string?)json["modules"]!["programs"]!["direct"]![1]!["path"]);
    }

    [Fact]
    public void Write_copies_everything_and_rewrites_profile()
    {
        var (fs, profile, inputs) = Arrange();
        inputs.Add(new("modules.programs.direct[1].path", @"C:\data\tool.msi", ExternalInputKind.File));
        var plan = ProfileBundle.Plan(profile, inputs, fs);
        long copied = 0;
        plan.Write(@"E:\FormatKit", @"C:\exe\formatkit.exe", fs, new Progress0(n => copied = n), CancellationToken.None);

        Assert.Equal("EXE", fs.ReadAllText(@"E:\FormatKit\formatkit.exe"));
        Assert.Equal("0123456789", fs.ReadAllText(@"E:\FormatKit\inputs\1-programs.json"));
        Assert.Equal("12345", fs.ReadAllText(@"E:\FormatKit\inputs\2-netdrivers\a.inf"));
        Assert.Equal("1234567", fs.ReadAllText(@"E:\FormatKit\inputs\2-netdrivers\sub\b.sys"));
        Assert.Equal("abc", fs.ReadAllText(@"E:\FormatKit\inputs\3-tool.msi"));

        var text = fs.ReadAllText(@"E:\FormatKit\profile.json");
        Assert.DoesNotContain("comment", text);
        var json = JsonNode.Parse(text)!;
        Assert.Equal(@"inputs\2-netdrivers", (string?)json["modules"]!["drivers"]!["networkDriverBackup"]);
        var direct = json["modules"]!["programs"]!["direct"]!;
        Assert.Equal("https://x/y.exe", (string?)direct[0]!["url"]);
        Assert.Null(direct[0]!["path"]);
        Assert.Equal(@"inputs\3-tool.msi", (string?)direct[1]!["path"]);
        Assert.Equal("pt-BR", (string?)json["setup"]!["language"]);
        Assert.Contains("\n", text);
        Assert.True(copied >= 25);
    }

    [Fact]
    public void Write_honors_cancellation()
    {
        var (fs, profile, inputs) = Arrange();
        var plan = ProfileBundle.Plan(profile, inputs, fs);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            plan.Write(@"E:\FormatKit", @"C:\exe\formatkit.exe", fs, null, cts.Token));
    }

    private sealed class Progress0(Action<long> a) : IProgress<long>
    {
        public void Report(long value) => a(value);
    }
}
