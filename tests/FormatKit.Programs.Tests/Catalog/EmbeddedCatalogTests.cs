using FormatKit.Programs.Catalog;

namespace FormatKit.Programs.Tests.Catalog;

public class EmbeddedCatalogTests
{
    // Spec section 7 entries, mapped to catalog ids.
    private static readonly (string Name, string Id)[] SpecEntries =
    [
        ("Chrome", "browsers.chrome"),
        ("Firefox", "browsers.firefox"),
        ("Brave", "browsers.brave"),
        ("Opera GX", "browsers.opera-gx"),
        ("Vivaldi", "browsers.vivaldi"),
        ("Discord", "communication.discord"),
        ("WhatsApp", "communication.whatsapp"),
        ("Telegram", "communication.telegram"),
        ("Zoom", "communication.zoom"),
        ("Slack", "communication.slack"),
        ("Steam", "games.steam"),
        ("Epic Games Launcher", "games.epic-games"),
        ("EA app", "games.ea-app"),
        ("Ubisoft Connect", "games.ubisoft-connect"),
        ("Battle.net", "games.battle-net"),
        ("GOG Galaxy", "games.gog-galaxy"),
        ("Riot Client", "games.riot-client"),
        ("Playnite", "games.playnite"),
        ("VLC", "media.vlc"),
        ("Spotify", "media.spotify"),
        ("OBS Studio", "media.obs-studio"),
        ("Audacity", "media.audacity"),
        ("HandBrake", "media.handbrake"),
        ("foobar2000", "media.foobar2000"),
        ("7-Zip", "utilities.7zip"),
        ("PowerToys", "utilities.powertoys"),
        ("Everything", "utilities.everything"),
        ("ShareX", "utilities.sharex"),
        ("Bitwarden", "utilities.bitwarden"),
        ("qBittorrent", "utilities.qbittorrent"),
        ("CPU-Z", "utilities.cpu-z"),
        ("HWiNFO", "utilities.hwinfo"),
        ("CrystalDiskInfo", "utilities.crystaldiskinfo"),
        ("LibreOffice", "productivity.libreoffice"),
        ("Adobe Acrobat Reader", "productivity.acrobat-reader"),
        ("Notion", "productivity.notion"),
        ("Obsidian", "productivity.obsidian"),
        ("Thunderbird", "productivity.thunderbird"),
        ("VS Code", "development.vscode"),
        ("Git", "development.git"),
        ("Python 3", "development.python"),
        ("Node.js LTS", "development.nodejs-lts"),
        ("Docker Desktop", "development.docker-desktop"),
        ("JetBrains Toolbox", "development.jetbrains-toolbox"),
    ];

    // Riot Client: winget has no standalone Riot Client package, only per-game, per-region ones.
    private static readonly string[] Dropped = ["games.riot-client"];

    // Discord, Zoom and Spotify per the spec; Playnite and JetBrains Toolbox because winget offers only a user-scope installer.
    private static readonly string[] PerUser =
    [
        "communication.discord", "communication.zoom", "media.spotify", "games.playnite", "development.jetbrains-toolbox",
    ];

    private static readonly string[] ExcludedKinds = ["cleaner", "driver", "booster", "antivirus"];

    private static readonly ProgramCatalog Catalog = ProgramCatalog.LoadEmbedded();

    [Fact]
    public void Catalog_is_valid()
    {
        Assert.Equal(1, Catalog.SchemaVersion);
        Assert.NotEmpty(Catalog.Programs);
        Assert.Empty(ProgramCatalogValidator.Validate(Catalog));
    }

    [Fact]
    public void Every_spec_entry_exists_or_is_listed_as_dropped()
    {
        foreach (var (name, id) in SpecEntries)
        {
            var present = Catalog.Find(id) is not null;
            var dropped = Dropped.Contains(id);
            Assert.True(present ^ dropped, $"{name} ({id}): present={present}, dropped={dropped}");
        }

        var expected = SpecEntries.Select(e => e.Id).Except(Dropped).Order(StringComparer.Ordinal);
        Assert.Equal(expected, Catalog.Programs.Select(p => p.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Per_user_entries_are_marked()
    {
        var expected = PerUser.Except(Dropped).Order(StringComparer.Ordinal);
        var actual = Catalog.Programs.Where(p => p.Context == InstallContext.User).Select(p => p.Id).Order(StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void No_excluded_kinds()
    {
        foreach (var program in Catalog.Programs)
        {
            var names = program.Text.Values.Select(t => t.Name).Append(program.Id);
            foreach (var kind in ExcludedKinds)
                Assert.DoesNotContain(names, n => n.Contains(kind, StringComparison.OrdinalIgnoreCase));
        }
    }
}
