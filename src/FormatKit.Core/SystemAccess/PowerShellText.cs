using System.Text;

namespace FormatKit.Core.SystemAccess;

public static class PowerShellText
{
    /// <summary>
    /// A PowerShell single-quoted literal. PowerShell's tokenizer treats U+2018, U+2019, U+201A and U+201B as
    /// single quotes too, so each of them is doubled like the ASCII one.
    /// </summary>
    public static string SingleQuote(string value)
    {
        var sb = new StringBuilder("'");
        foreach (var c in value)
        {
            sb.Append(c);
            if (c is '\'' or '‘' or '’' or '‚' or '‛')
            {
                sb.Append(c);
            }
        }

        return sb.Append('\'').ToString();
    }
}
