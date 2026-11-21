using System.Text;
using MarkTop.Core.Documents;
using Xunit;

namespace MarkTop.Core.Tests;

public sealed class DocumentRoundTripTests
{
    [Theory]
    [InlineData("你好，MarkTop\r\n第二行\r\n", LineEndings.CrLf)]
    [InlineData("line one\nline two\n", LineEndings.Lf)]
    [InlineData("混合\r\n换行\n测试\r", LineEndings.CrLf)]
    public void LineEndingsAreDetectedAndNormalized(string text, string expected)
    {
        var detected = LineEndings.Detect(text);
        Assert.Equal(expected, detected);

        var normalized = LineEndings.Normalize(text, detected);
        var unwanted = detected == LineEndings.CrLf ? "\n" : "\r";
        Assert.DoesNotContain(unwanted, normalized.Replace(detected, string.Empty));
        Assert.EndsWith(detected, normalized);
    }

    [Fact]
    public void CrlfIsDetectedOverLf()
    {
        Assert.Equal(LineEndings.CrLf, LineEndings.Detect("a\r\nb\r\nc"));
        Assert.Equal(LineEndings.Lf, LineEndings.Detect("a\nb\nc"));
        Assert.Equal("CRLF", LineEndings.Describe("\r\n"));
    }

    [Fact]
    public void Utf8BomIsPreservedOnSave()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "bom.md");
        File.WriteAllBytes(path, new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("# 标题\r\n")).ToArray());

        var session = DocumentSession.Open(path);
        Assert.True(session.Encoding.HasBom);
        Assert.Equal(LineEndings.CrLf, session.LineEnding);

        session.Save(session.OriginalText);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    }

    [Fact]
    public void Utf16WithoutBomDetectionFallsBackToGb18030()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gb = Encoding.GetEncoding(54936);
        var bytes = gb.GetBytes("中文测试");
        var detected = TextEncodingDetector.Detect(bytes);
        var text = TextEncodingDetector.Decode(bytes, detected, out _);
        Assert.Equal("中文测试", text.TrimEnd('\uFFFD'));
    }

    [Fact]
    public void SaveKeepsLineEndings()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "crlf.md");
        File.WriteAllText(path, "a\r\nb\r\n", new UTF8Encoding(false));

        var session = DocumentSession.Open(path);
        session.Save("a\nb\nc\n");

        var saved = File.ReadAllText(path);
        Assert.Equal("a\r\nb\r\nc\r\n", saved);
    }

    [Fact]
    public void KindClassificationMatchesSpec()
    {
        Assert.Equal(DocumentKind.Markdown, DocumentClassifier.Classify("a.MD"));
        Assert.Equal(DocumentKind.Json, DocumentClassifier.Classify("a.json"));
        Assert.Equal(DocumentKind.Xml, DocumentClassifier.Classify("a.xml"));
        Assert.Equal(DocumentKind.Yaml, DocumentClassifier.Classify("a.yml"));
        Assert.Equal(DocumentKind.Yaml, DocumentClassifier.Classify("a.yaml"));
        Assert.Equal(DocumentKind.Lang, DocumentClassifier.Classify("a.lang"));
        Assert.Equal(DocumentKind.PlainText, DocumentClassifier.Classify("a.txt"));
        Assert.False(DocumentClassifier.SupportsRender("a.cs"));
        Assert.True(DocumentClassifier.SupportsRender("a.md"));
    }

    internal static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "marktop-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
