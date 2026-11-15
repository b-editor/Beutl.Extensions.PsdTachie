using System.Text;

namespace Beutl.Extensions.PsdTachie.Psd;

internal static class JapaneseText
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);
    private static readonly Encoding s_shiftJis = CodePagesEncodingProvider.Instance.GetEncoding(932)!;

    public static string Decode(byte[] bytes)
    {
        try
        {
            return s_utf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return s_shiftJis.GetString(bytes);
        }
    }
}
