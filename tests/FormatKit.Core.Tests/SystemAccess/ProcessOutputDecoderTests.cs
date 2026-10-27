using System.Text;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Core.Tests.SystemAccess;

public class ProcessOutputDecoderTests
{
    private static readonly Encoding Oem850 = CodePagesEncodingProvider.Instance.GetEncoding(850)!;

    [Fact]
    public void Utf8_output_is_decoded_as_utf8()
    {
        var bytes = Encoding.UTF8.GetBytes("ÊXITO: tarefa excluída");

        Assert.Equal("ÊXITO: tarefa excluída", ProcessOutputDecoder.Decode(bytes, Oem850));
    }

    [Fact]
    public void Oem_output_falls_back_to_the_oem_code_page()
    {
        var bytes = Oem850.GetBytes("Utilitário PnP da Microsoft: Diretório ausente");

        Assert.Equal("Utilitário PnP da Microsoft: Diretório ausente", ProcessOutputDecoder.Decode(bytes, Oem850));
    }

    [Fact]
    public void Utf8_byte_order_mark_is_dropped()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("ok")).ToArray();

        Assert.Equal("ok", ProcessOutputDecoder.Decode(bytes, Oem850));
    }

    [Fact]
    public void Empty_output_is_empty()
    {
        Assert.Equal("", ProcessOutputDecoder.Decode([], Oem850));
    }
}
