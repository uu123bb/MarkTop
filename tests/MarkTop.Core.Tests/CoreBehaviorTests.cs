using MarkTop.Core.Configuration;
using MarkTop.Core.FileSystem;
using MarkTop.Core.Git;
using MarkTop.Core.Export;
using Xunit;

namespace MarkTop.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void MissingFileYieldsDefaults()
    {
        var directory = DocumentRoundTripTests.CreateTempDirectory();
        var path = Path.Combine(directory, "setting-marktop.json");
        var store = new SettingsStore(path);
        Assert.Equal("dark", store.Settings.Theme);
        Assert.Null(store.LoadWarning);
    }

    [Fact]
    public void CorruptFileIsBackedUpAndDefaultsUsed()
    {
        var directory = DocumentRoundTripTests.CreateTempDirectory();
        var path = Path.Combine(directory, "setting-marktop.json");
        File.WriteAllText(path, "{ not json ");

        var store = new SettingsStore(path);
        Assert.NotNull(store.LoadWarning);
        Assert.Equal("dark", store.Settings.Theme);
        Assert.Single(Directory.GetFiles(directory, "setting-marktop.json.bad-*"));
    }

    [Fact]
    public void SaveWritesCamelCaseJsonNextToExe()
    {
        var directory = DocumentRoundTripTests.CreateTempDirectory();
        var path = Path.Combine(directory, "setting-marktop.json");
        var store = new SettingsStore(path);
        store.Settings.Theme = "light";
        Assert.True(store.Save());

        var json = File.ReadAllText(path);
        Assert.Contains("\"theme\": \"light\"", json);
        Assert.Contains("\"schemaVersion\"", json);
        Assert.True(File.Exists(path));
        Assert.Single(Directory.GetFiles(directory, "*.json"));
    }

    [Fact]
    public void NormalizeClampsInvalidValues()
    {
        var settings = new AppSettings { Theme = "PURPLE" };
        settings.Normalize();
        Assert.Equal("dark", settings.Theme);
        Assert.Equal(GitSettings.DefaultCommitMessage, settings.Git.CommitMessage);
    }
}

public sealed class AssetStoreTests
{
    [Fact]
    public void ImageIsStoredUnderAssetsWithRelativePath()
    {
        var directory = DocumentRoundTripTests.CreateTempDirectory();
        var document = Path.Combine(directory, "note.md");
        File.WriteAllText(document, "# x");

        var relative = AssetStore.SaveImage(document, "屏幕 截图.png", new byte[] { 1, 2, 3, 4 });

        Assert.StartsWith("assets/", relative);
        Assert.EndsWith(".png", relative);
        var absolute = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(absolute));
    }

    [Fact]
    public void SameImageIsNotDuplicated()
    {
        var directory = DocumentRoundTripTests.CreateTempDirectory();
        var document = Path.Combine(directory, "note.md");
        var bytes = new byte[] { 9, 9, 9 };
        var first = AssetStore.SaveImage(document, "shot.png", bytes);
        var second = AssetStore.SaveImage(document, "shot.png", bytes);
        Assert.Equal(first, second);
        Assert.Single(Directory.GetFiles(Path.Combine(directory, "assets")));
    }

    [Fact]
    public void PathGuardRejectsEscapes()
    {
        var root = Path.Combine(Path.GetTempPath(), "marktop-root");
        Assert.True(PathGuard.IsWithin(root, Path.Combine(root, "a", "b.txt")));
        Assert.False(PathGuard.IsWithin(root, Path.Combine(root, "..", "b.txt")));
        Assert.True(PathGuard.IsWithin(root, root));
    }
}

public sealed class GitErrorTests
{
    [Theory]
    [InlineData("fatal: Authentication failed for 'https://github.com/a/a.git'", GitErrorKind.Auth)]
    [InlineData("remote: Repository not found.", GitErrorKind.RepositoryNotFound)]
    [InlineData("! [rejected] main -> main (non-fast-forward)", GitErrorKind.NonFastForward)]
    [InlineData("fatal: unable to access 'https://github.com/a/a.git/': Could not resolve host: github.com", GitErrorKind.Network)]
    [InlineData("error: Your local changes to the following files would be overwritten by merge", GitErrorKind.DirtyWorkTree)]
    public void ClassifiesCommonFailures(string stderr, GitErrorKind expected)
    {
        var result = new GitResult(1, string.Empty, stderr);
        Assert.Equal(expected, GitErrors.Classify(result).Kind);
    }

    [Fact]
    public void SuccessHasNoError()
    {
        Assert.Equal(GitErrorKind.None, GitErrors.Classify(new GitResult(0, "ok", string.Empty)).Kind);
    }
}

public sealed class HtmlImporterTests
{
    [Fact]
    public void ConvertsStructureAndStripsScripts()
    {
        const string html = """
            <html><head><style>p{color:red}</style></head>
            <body>
              <h1>标题</h1>
              <p>段落 <strong>加粗</strong> <a href="https://example.com">链接</a></p>
              <ul><li>一</li><li>二</li></ul>
              <table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>
              <script>alert('x')</script>
              <iframe src="https://evil"></iframe>
            </body></html>
            """;

        var markdown = HtmlImporter.ToMarkdown(html);

        Assert.Contains("# 标题", markdown);
        Assert.Contains("**加粗**", markdown);
        Assert.Contains("https://example.com", markdown);
        Assert.Contains("| A | B |", markdown);
        Assert.DoesNotContain("alert", markdown);
        Assert.DoesNotContain("<script", markdown);
        Assert.DoesNotContain("iframe", markdown);
    }
}
