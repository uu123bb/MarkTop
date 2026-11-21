using System.Text;

namespace MarkTop.Core.Documents;

public sealed record DetectedEncoding(Encoding Encoding, bool HasBom, bool LossyFallback)
{
    public string DisplayName => Encoding.WebName.ToUpperInvariant() switch
    {
        "UTF-8" => HasBom ? "UTF-8 BOM" : "UTF-8",
        "UTF-16" => HasBom ? "UTF-16 BOM" : "UTF-16",
        "UTF-32" => "UTF-32",
        "GB18030" => "GB18030",
        var other => other,
    };
}

public static class TextEncodingDetector
{
    static TextEncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static DetectedEncoding Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new DetectedEncoding(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), true, false);
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            return new DetectedEncoding(new UTF32Encoding(bigEndian: false, byteOrderMark: true), true, false);
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            return new DetectedEncoding(new UTF32Encoding(bigEndian: true, byteOrderMark: true), true, false);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return new DetectedEncoding(new UnicodeEncoding(bigEndian: false, byteOrderMark: true), true, false);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return new DetectedEncoding(new UnicodeEncoding(bigEndian: true, byteOrderMark: true), true, false);
        }

        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            strictUtf8.GetString(bytes);
            return new DetectedEncoding(strictUtf8, false, false);
        }
        catch (DecoderFallbackException)
        {
            // Chinese Windows files are frequently GBK/GB18030. Decoding as GB18030 is lossless for GBK.
            try
            {
                var gb = Encoding.GetEncoding(54936, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
                return new DetectedEncoding(gb, false, true);
            }
            catch
            {
                return new DetectedEncoding(new UTF8Encoding(false, false), false, true);
            }
        }
    }

    public static string Decode(ReadOnlySpan<byte> bytes, DetectedEncoding detected, out int bomLength)
    {
        bomLength = detected.HasBom ? detected.Encoding.GetPreamble().Length : 0;
        var payload = bytes[bomLength..];
        return detected.Encoding.GetString(payload);
    }

    public static byte[] Encode(string text, DetectedEncoding detected)
    {
        var body = detected.Encoding.GetBytes(text);
        if (!detected.HasBom)
        {
            return body;
        }

        var preamble = detected.Encoding.GetPreamble();
        var buffer = new byte[preamble.Length + body.Length];
        preamble.CopyTo(buffer, 0);
        body.CopyTo(buffer, preamble.Length);
        return buffer;
    }
}
