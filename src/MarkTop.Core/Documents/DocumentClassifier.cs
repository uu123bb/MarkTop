namespace MarkTop.Core.Documents;

public enum DocumentKind
{
    Markdown,
    Json,
    Xml,
    Yaml,
    Lang,
    PlainText,
}

public static class DocumentClassifier
{
    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".mdown", ".mkd", ".mdx",
    };

    public static DocumentKind Classify(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
        {
            return DocumentKind.PlainText;
        }

        if (MarkdownExtensions.Contains(extension))
        {
            return DocumentKind.Markdown;
        }

        return extension.ToLowerInvariant() switch
        {
            ".json" or ".jsonc" => DocumentKind.Json,
            ".xml" or ".xaml" or ".xsd" or ".svg" or ".csproj" or ".config" => DocumentKind.Xml,
            ".yml" or ".yaml" => DocumentKind.Yaml,
            ".lang" => DocumentKind.Lang,
            _ => DocumentKind.PlainText,
        };
    }

    public static bool IsMarkdown(string path) => Classify(path) == DocumentKind.Markdown;

    /// <summary>Rendering is only enabled for Markdown; every other kind is plain-text editing.</summary>
    public static bool SupportsRender(string path) => IsMarkdown(path);
}
