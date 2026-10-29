using System.Text;

namespace FormatKit.Dev;

/// <summary>Builds the two WSL config files; both are written with LF endings.</summary>
internal static class WslConf
{
    /// <summary>Returns <paramref name="existing"/> with <c>[user] default=</c> set, keeping every other section and key.</summary>
    public static string SetDefaultUser(string existing, string user)
    {
        var wanted = $"default={user}";
        var text = existing.Replace("\r\n", "\n");
        var lines = text.Split('\n').ToList();

        var header = lines.FindIndex(l => l.Trim().Equals("[user]", StringComparison.OrdinalIgnoreCase));
        if (header < 0)
        {
            var body = text.TrimEnd('\n');
            return body.Length == 0 ? $"[user]\n{wanted}\n" : $"{body}\n\n[user]\n{wanted}\n";
        }

        var end = lines.FindIndex(header + 1, l => l.TrimStart().StartsWith('['));
        if (end < 0)
            end = lines.Count;

        var key = lines.FindIndex(header + 1, end - header - 1, IsDefaultKey);
        if (key >= 0)
        {
            if (lines[key].Trim() == wanted)
                return existing;
            lines[key] = wanted;
        }
        else
        {
            lines.Insert(header + 1, wanted);
        }

        return string.Join('\n', lines);
    }

    /// <summary><c>%USERPROFILE%\.wslconfig</c>: a <c>[wsl2]</c> section with the keys that were given.</summary>
    public static string WslConfig(WslConfig config)
    {
        var sb = new StringBuilder("[wsl2]\n");
        if (config.Memory is not null)
            sb.Append("memory=").Append(config.Memory).Append('\n');
        if (config.Processors is not null)
            sb.Append("processors=").Append(config.Processors.Value).Append('\n');
        if (config.Swap is not null)
            sb.Append("swap=").Append(config.Swap).Append('\n');
        return sb.ToString();
    }

    private static bool IsDefaultKey(string line)
    {
        var eq = line.IndexOf('=');
        return eq > 0 && line[..eq].Trim().Equals("default", StringComparison.OrdinalIgnoreCase);
    }
}
