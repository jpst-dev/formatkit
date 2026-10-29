using System.Globalization;
using System.Text.Json;
using FormatKit.Core.Profiles;
using FormatKit.Core.Tests.Fakes;
using FormatKit.Prepare.Checks.Developer;

namespace FormatKit.Prepare.Tests.Checks.Developer;

public class KeysCheckTests : IDisposable
{
    private const string Ssh = @"C:\Users\joao\.ssh";
    private const string Gnupg = @"C:\Users\joao\AppData\Roaming\gnupg\private-keys-v1.d";
    private const string PrivateBody = "-----BEGIN OPENSSH PRIVATE KEY-----\nSECRETLINE2\nSECRETLINE3\n";

    private readonly CultureInfo _saved = CultureInfo.CurrentUICulture;

    public KeysCheckTests() => CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _saved;

    private static LoadedProfile Profile(string devJson) =>
        new(new Profile(1, "test",
            new Dictionary<string, JsonElement> { ["dev"] = JsonDocument.Parse(devJson).RootElement.Clone() }),
            @"C:\p.json", "hash");

    private static Task<IReadOnlyList<PrepareItem>> RunAsync(InMemoryFileSystem files, LoadedProfile? profile = null) =>
        new KeysCheck().RunAsync(
            PrepareTestContext.Create(TestServices.Create(files: files), profile: profile), CancellationToken.None);

    private static InMemoryFileSystem SshFiles()
    {
        var files = new InMemoryFileSystem();
        files.Seed($@"{Ssh}\id_ed25519", PrivateBody);
        files.Seed($@"{Ssh}\id_ed25519.pub", "ssh-ed25519 AAAA joao");
        files.Seed($@"{Ssh}\deploy", PrivateBody);
        files.Seed($@"{Ssh}\known_hosts", "github.com ssh-ed25519 AAAA");
        return files;
    }

    [Fact]
    public void Identity()
    {
        var check = new KeysCheck();
        Assert.Equal("dev.keys", check.Id);
        Assert.Equal(PrepareGroup.Developer, check.Group);
    }

    [Fact]
    public async Task Ssh_private_keys_give_one_attention_with_the_count_and_backup_text()
    {
        var item = Assert.Single(await RunAsync(SshFiles()));

        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Equal("dev.keys", item.CheckId);
        Assert.Contains("2", item.Title);
        Assert.Contains("id_ed25519", item.Detail);
        Assert.Contains("deploy", item.Detail);
        Assert.DoesNotContain("known_hosts", item.Detail);
        Assert.DoesNotContain(".pub", item.Detail);
        Assert.Contains("Back up", item.Action);
    }

    [Fact]
    public async Task Profile_with_dev_ssh_says_it_creates_a_new_key()
    {
        var item = Assert.Single(await RunAsync(SshFiles(), Profile("""{"enabled":true,"ssh":{"email":"a@b.c"}}""")));

        Assert.Contains("profile creates a new", item.Action);
        Assert.Contains("GitHub", item.Action);
    }

    [Theory]
    [InlineData("""{"enabled":false,"ssh":{}}""")]
    [InlineData("""{"enabled":true}""")]
    public async Task Profile_without_an_enabled_ssh_section_keeps_the_backup_text(string dev)
    {
        var item = Assert.Single(await RunAsync(SshFiles(), Profile(dev)));

        Assert.Contains("Back up", item.Action);
    }

    [Fact]
    public async Task Only_the_first_line_of_a_non_id_file_decides_and_no_secret_reaches_an_item()
    {
        var files = new InMemoryFileSystem();
        files.Seed($@"{Ssh}\deploy", PrivateBody);
        files.Seed($@"{Ssh}\notes", "just notes\n-----BEGIN OPENSSH PRIVATE KEY-----\n");

        var item = Assert.Single(await RunAsync(files));

        Assert.Contains("1", item.Title);
        Assert.DoesNotContain("notes", item.Detail);
        Assert.All(new[] { item.Title, item.Detail, item.Action }, t =>
        {
            Assert.DoesNotContain("SECRETLINE", t);
            Assert.DoesNotContain("BEGIN", t);
        });
        Assert.Contains($@"open:{Ssh}\deploy", files.Events);
    }

    [Fact]
    public async Task Files_named_id_are_not_opened()
    {
        var files = SshFiles();

        await RunAsync(files);

        Assert.DoesNotContain(files.Events, e => e.Contains("id_ed25519", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Gpg_secret_keys_give_attention_with_the_export_command()
    {
        var files = new InMemoryFileSystem();
        files.Seed($@"{Gnupg}\x.key", "secret");

        var item = Assert.Single(await RunAsync(files));

        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Contains("GPG", item.Title);
        Assert.Contains("gpg --export-secret-keys", item.Action);
    }

    [Fact]
    public async Task Ssh_and_gpg_together_give_two_items()
    {
        var files = SshFiles();
        files.Seed($@"{Gnupg}\x.key", "secret");

        Assert.Equal(2, (await RunAsync(files)).Count);
    }

    [Fact]
    public async Task Nothing_gives_no_item()
    {
        var files = new InMemoryFileSystem();
        files.Seed($@"{Ssh}\known_hosts", "github.com ssh-ed25519 AAAA");
        files.Seed($@"{Ssh}\config", "Host *");

        Assert.Empty(await RunAsync(files));
    }

    [Fact]
    public async Task Unreadable_ssh_folder_is_not_checked()
    {
        var files = new InMemoryFileSystem();
        files.SeedDirectory(Ssh);
        files.MarkUnreadable(Ssh);

        var item = Assert.Single(await RunAsync(files));

        Assert.Equal(PrepareStatus.NotChecked, item.Status);
    }

    [Fact]
    public async Task PuTTY_private_keys_are_detected_by_extension_or_first_line()
    {
        var files = new InMemoryFileSystem();
        files.Seed($@"{Ssh}\work.ppk", "PuTTY-User-Key-File-3: ssh-ed25519\nEncryption: none\n");
        files.Seed($@"{Ssh}\server", "PuTTY-User-Key-File-2: ssh-rsa\nEncryption: none\n");
        files.Seed($@"{Ssh}\config", "Host github.com\n");

        var item = Assert.Single(await RunAsync(files));

        Assert.Equal(PrepareStatus.Attention, item.Status);
        Assert.Contains("work.ppk", item.Detail);
        Assert.Contains("server", item.Detail);
        Assert.DoesNotContain("config", item.Detail);
    }
}
