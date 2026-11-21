using System.Text.Json.Serialization;

namespace MarkTop.Core.Configuration;

/// <summary>
/// The whole persisted state of MarkTop. It lives in a single file next to the executable:
/// <c>setting-marktop.json</c>. Nothing else is ever written beside the executable.
/// </summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>"dark" or "light".</summary>
    public string Theme { get; set; } = "dark";

    public EditorSettings Editor { get; set; } = new();

    public ExportSettings Export { get; set; } = new();

    public GitSettings Git { get; set; } = new();

    public WindowSettings Window { get; set; } = new();

    public List<string> RecentFiles { get; set; } = new();

    public string? LastWorkspace { get; set; }

    public AppSettings Normalize()
    {
        Theme = string.Equals(Theme, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
        Editor ??= new EditorSettings();
        Editor.Mode = Editor.Mode is "source" ? "source" : "split";
        Export ??= new ExportSettings();
        Git ??= new GitSettings();
        Window ??= new WindowSettings();
        RecentFiles ??= new();
        if (RecentFiles.Count > 20)
        {
            RecentFiles = RecentFiles.Take(20).ToList();
        }

        Git.CloneRoot ??= string.Empty;
        Git.UserName ??= string.Empty;
        Git.CommitMessage ??= GitSettings.DefaultCommitMessage;
        return this;
    }
}

public sealed class EditorSettings
{
    /// <summary>"split"（源码 + 预览）或 "source"（仅源码）。旧的 ir / immersive 会回落到 split。</summary>
    public string Mode { get; set; } = "split";

    public string FontFamily { get; set; } = "Cascadia Mono, Consolas, Microsoft YaHei UI";

    public double FontSize { get; set; } = 14;

    public int TabSize { get; set; } = 4;

    public bool WordWrap { get; set; } = true;

    public bool LineNumbers { get; set; } = true;

    public bool LintEnabled { get; set; } = true;

    public bool ScrollSync { get; set; } = true;
}

public sealed class ExportSettings
{
    /// <summary>Page margin in millimetres for PDF output.</summary>
    public double PdfMarginMm { get; set; } = 18;

    public bool EmbedImagesAsBase64 { get; set; } = false;
}

public sealed class GitSettings
{
    public const string DefaultCommitMessage = "docs: update profile README";

    /// <summary>GitHub user name, also used to build https://github.com/&lt;user&gt;/&lt;user&gt;.git</summary>
    public string UserName { get; set; } = string.Empty;

    public string CommitMessage { get; set; } = DefaultCommitMessage;

    /// <summary>Where profile repositories are cloned. Empty means %LOCALAPPDATA%\MarkTop\repos.</summary>
    public string CloneRoot { get; set; } = string.Empty;

    public bool AutoPush { get; set; } = true;
}

public sealed class WindowSettings
{
    public double Width { get; set; } = 1280;

    public double Height { get; set; } = 820;

    public double? Left { get; set; }

    public double? Top { get; set; }

    [JsonPropertyName("maximized")]
    public bool Maximized { get; set; }

    public bool SidebarVisible { get; set; } = true;
}
