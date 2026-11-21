using System.Text;

namespace MarkTop.Core.Documents;

public static class LineEndings
{
    public const string CrLf = "\r\n";
    public const string Lf = "\n";
    public const string Cr = "\r";

    public static string Detect(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        if (crlf >= lf && crlf >= cr && crlf > 0)
        {
            return CrLf;
        }

        if (lf >= cr && lf > 0)
        {
            return Lf;
        }

        return cr > 0 ? Cr : Environment.NewLine;
    }

    public static string Normalize(string text, string target)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                builder.Append(target);
            }
            else if (c == '\n')
            {
                builder.Append(target);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    public static string Describe(string eol) => eol switch
    {
        CrLf => "CRLF",
        Lf => "LF",
        Cr => "CR",
        _ => eol.Length == 2 ? "CRLF" : "LF",
    };
}
