using System.Text.Json;
using FormatKit.Programs.Catalog;

namespace FormatKit.Programs.Tests;

internal static class ProgramsTestData
{
    public const string ExportSample = """
        {"$schema":"https://aka.ms/winget-packages.schema.2.0.json","CreationDate":"2026-10-28T10:00:00.000-00:00","Sources":[{"Packages":[{"PackageIdentifier":"Git.Git"}],"SourceDetails":{"Argument":"https://cdn.winget.microsoft.com/cache","Identifier":"Microsoft.Winget.Source_8wekyb3d8bbwe","Name":"winget","Type":"Microsoft.PreIndexed.Package"}},{"Packages":[{"PackageIdentifier":"9NKSQGP7F2NH"}],"SourceDetails":{"Argument":"https://storeedgefd.dsx.mp.microsoft.com/v9.0","Identifier":"StoreEdgeFD","Name":"msstore","Type":"Microsoft.Rest"}}],"WinGetVersion":"1.9.25200"}
        """;

    public static readonly string Sha = new('a', 64);

    public static ProgramCatalog TestCatalog() => ProgramCatalog.Parse("""
        { "schemaVersion": 1, "programs": [
          { "id": "browsers.firefox", "category": "browsers", "wingetId": "Mozilla.Firefox", "source": "winget", "scope": "machine",
            "text": { "en": { "name": "Firefox", "description": "Browser" }, "pt-BR": { "name": "Firefox", "description": "Navegador" } } },
          { "id": "communication.discord", "category": "communication", "wingetId": "Discord.Discord", "source": "winget", "context": "user", "scope": "user",
            "text": { "en": { "name": "Discord", "description": "Chat" }, "pt-BR": { "name": "Discord", "description": "Bate-papo" } } },
          { "id": "games.steam", "category": "games", "wingetId": "Valve.Steam", "source": "winget",
            "text": { "en": { "name": "Steam", "description": "Games" }, "pt-BR": { "name": "Steam", "description": "Jogos" } } }
        ] }
        """);

    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
