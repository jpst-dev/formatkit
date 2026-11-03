using System.Text.Json;

namespace FormatKit.Core.SystemAccess;

/// <summary>
/// Reads image facts with Get-WindowsImage (numeric architecture, version string, language codes), never from dism.exe
/// text, and splits with dism.exe /Split-Image (exit code only).
/// </summary>
public sealed class PowerShellWindowsImages(IProcessRunner processes) : IWindowsImages
{
    public async Task<IReadOnlyList<WindowsImageInfo>> ListAsync(string imagePath, CancellationToken ct)
    {
        var path = PowerShellText.SingleQuote(imagePath);
        var script =
            "$ErrorActionPreference = 'Stop'; " +
            $"$items = @(Get-WindowsImage -ImagePath {path} | ForEach-Object {{ " +
            $"$d = Get-WindowsImage -ImagePath {path} -Index $_.ImageIndex; " +
            "[pscustomobject]@{ Index = [int]$d.ImageIndex; Name = [string]$d.ImageName; Architecture = [int]$d.Architecture; " +
            "Version = [string]$d.Version; Languages = @($d.Languages | ForEach-Object { [string]$_ }); " +
            "DefaultLanguageIndex = [int]$d.DefaultLanguageIndex } }); " +
            "ConvertTo-Json -InputObject $items -Compress";

        var result = await processes.RunAsync(
            new ProcessSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], Timeouts.Query),
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException($"Reading the Windows image failed (exit {result.ExitCode}).");
        }

        if (string.IsNullOrWhiteSpace(result.StdOut))
        {
            throw new InvalidOperationException($"Reading the Windows image printed nothing (exit {result.ExitCode}).");
        }

        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            var root = doc.RootElement;
            var list = new List<WindowsImageInfo>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    list.Add(Parse(item));
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                list.Add(Parse(root));
            }
            else
            {
                throw new JsonException("Unexpected JSON root.");
            }

            return list;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException($"Unexpected Windows image output (exit {result.ExitCode}).", ex);
        }
    }

    public async Task SplitAsync(string wimPath, string swmPath, int partSizeMb, CancellationToken ct)
    {
        // Split-WindowsImage opens the source for writing, which a mounted ISO refuses (access denied);
        // dism.exe /Split-Image reads it read-only. Only the exit code is used, never the text.
        var arguments = new[]
        {
            "/English", "/Split-Image", $"/ImageFile:{wimPath}", $"/SWMFile:{swmPath}",
            $"/FileSize:{partSizeMb.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
        };

        var result = await processes.RunAsync(new ProcessSpec("dism.exe", arguments, Timeouts.Media), ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException($"Splitting the Windows image failed (exit {result.ExitCode}).");
        }
    }

    private static WindowsImageInfo Parse(JsonElement e)
    {
        var languages = new List<string>();
        if (e.TryGetProperty("Languages", out var l))
        {
            if (l.ValueKind == JsonValueKind.Array)
            {
                languages.AddRange(l.EnumerateArray().Select(x => x.GetString() ?? throw new JsonException("Null language.")));
            }
            else if (l.ValueKind == JsonValueKind.String)
            {
                languages.Add(l.GetString()!);
            }
        }

        return new WindowsImageInfo(
            e.GetProperty("Index").GetInt32(),
            e.GetProperty("Name").GetString() ?? "",
            e.GetProperty("Architecture").GetInt32(),
            e.GetProperty("Version").GetString() ?? "",
            languages,
            e.TryGetProperty("DefaultLanguageIndex", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : 0);
    }
}
