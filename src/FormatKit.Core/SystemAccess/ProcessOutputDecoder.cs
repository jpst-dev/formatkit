using System.Runtime.InteropServices;
using System.Text;

namespace FormatKit.Core.SystemAccess;

/// <summary>
/// Decodes redirected output of native tools. Some write UTF-8 (schtasks), others the console's OEM code page
/// (pnputil writes "Utilitário" as cp850 on pt-BR), so strict UTF-8 is tried first and the OEM page is the fallback.
/// </summary>
internal static class ProcessOutputDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static Encoding OemEncoding { get; } = CodePagesEncodingProvider.Instance.GetEncoding((int)GetOEMCP()) ?? Encoding.Latin1;

    public static string Decode(ReadOnlySpan<byte> bytes, Encoding fallback)
    {
        if (bytes.StartsWith(Encoding.UTF8.Preamble))
            bytes = bytes[Encoding.UTF8.Preamble.Length..];

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return fallback.GetString(bytes);
        }
    }

    public static async Task<string> ReadAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        return Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), OemEncoding);
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();
}
