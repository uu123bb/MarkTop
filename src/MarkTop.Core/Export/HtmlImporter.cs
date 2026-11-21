using System.Text.RegularExpressions;
using ReverseMarkdown;

namespace MarkTop.Core.Export;

/// <summary>Converts HTML into Markdown, dropping the parts a Markdown document cannot represent.</summary>
public static partial class HtmlImporter
{
    [GeneratedRegex(@"<script\b[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex(@"<style\b[^>]*>.*?</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleRegex();

    [GeneratedRegex(@"<iframe\b[^>]*>.*?</iframe>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex IframeRegex();

    [GeneratedRegex(@"\son[a-z]+\s*=\s*""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex InlineHandlerRegex();

    [GeneratedRegex(@"<(nav|footer|form)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ChromeRegex();

    public static string ToMarkdown(string html)
    {
        var config = new Config
        {
            GithubFlavored = true,
            RemoveComments = true,
            SmartHrefHandling = true,
            UnknownTags = Config.UnknownTagsOption.Bypass,
        };

        var converter = new Converter(config);
        return converter.Convert(Sanitize(html)).Trim() + Environment.NewLine;
    }

    /// <summary>Removes executable or layout-only markup before conversion.</summary>
    public static string Sanitize(string html)
    {
        var result = ScriptRegex().Replace(html, string.Empty);
        result = StyleRegex().Replace(result, string.Empty);
        result = IframeRegex().Replace(result, string.Empty);
        result = ChromeRegex().Replace(result, string.Empty);
        result = InlineHandlerRegex().Replace(result, string.Empty);
        return result;
    }
}
