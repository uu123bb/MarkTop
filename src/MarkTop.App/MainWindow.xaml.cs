using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MarkTop.App.Web;
using MarkTop.Core.Configuration;
using MarkTop.Core.Documents;
using MarkTop.Core.FileSystem;
using MarkTop.Core.Git;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace MarkTop.App;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settings;
    private readonly GitClient _git;
    private readonly ReadmeBootstrapService _profileService;
    private readonly Dictionary<int, TaskCompletionSource<string>> _textRequests = new();
    private readonly Dictionary<int, TaskCompletionSource<bool>> _pdfRequests = new();
    private readonly Dictionary<int, TaskCompletionSource<string>> _htmlRequests = new();

    private WebBridge? _bridge;
    private DocumentSession? _document;
    private DocTab? _activeTab;
    private ProfileRepo? _profileRepo;
    private string _docId = string.Empty;
    private string _assetsDir = string.Empty;
    private string? _workspace;
    private bool _dirty;
    private bool _suppressModeEvents;
    private int _requestCounter;

    public ObservableCollection<DocTab> Tabs { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _settings = new SettingsStore();
        if (!File.Exists(_settings.Path))
        {
            // The config file is the only artifact allowed next to the exe; create it on first run.
            _settings.Save();
        }

        _git = new GitClient();
        _profileService = new ReadmeBootstrapService(_git, _settings.Settings.Git.CloneRoot);

        ApplyTheme(_settings.Settings.Theme);
        RestoreWindowState();

        if (!string.IsNullOrWhiteSpace(_settings.LoadWarning))
        {
            StatusPath.Text = _settings.LoadWarning;
        }

        Loaded += OnLoadedAsync;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            await InitializeEditorAsync();
        }
        catch (Exception ex)
        {
            App.LogInfo($"init failed: {ex}");
            LoadingOverlay.Visibility = Visibility.Collapsed;
            MessageBox.Show(this,
                $"编辑器初始化失败：\n\n{ex.Message}\n\n请确认已安装 WebView2 运行时（Windows 10/11 通常自带）。",
                "MarkTop", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task InitializeEditorAsync()
    {
        App.LogInfo("init: start");
        var assetsDir = WebAssetInstaller.EnsureInstalled(out var assetsWarning);
        App.LogInfo($"init: assets at {assetsDir} ({assetsWarning ?? "ok"})");
        _assetsDir = assetsDir;
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MarkTop", "WebView2");
        Directory.CreateDirectory(userDataFolder);

        var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        App.LogInfo("init: environment ready");
        await EditorWebView.EnsureCoreWebView2Async(environment);
        App.LogInfo("init: core ready");

        var core = EditorWebView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.IsZoomControlEnabled = false;

        core.SetVirtualHostNameToFolderMapping("app.local", assetsDir, CoreWebView2HostResourceAccessKind.DenyCors);

        _bridge = new WebBridge(core);
        RegisterBridgeHandlers();

        _bridge.Send("boot", new { assets = assetsWarning });
        core.Navigate("https://app.local/index.html");
        App.LogInfo("init: navigated");

        if (!string.IsNullOrWhiteSpace(assetsWarning))
        {
            StatusPath.Text = assetsWarning;
        }

        if (!string.IsNullOrWhiteSpace(_settings.Settings.LastWorkspace) &&
            Directory.Exists(_settings.Settings.LastWorkspace))
        {
            LoadWorkspace(_settings.Settings.LastWorkspace);
        }
    }

    private void RegisterBridgeHandlers()
    {
        if (_bridge is null)
        {
            return;
        }

        _bridge.On("ready", _ =>
        {
            App.LogInfo($"web ready (assets: {_assetsDir})");
            SendInit();
        });
        _bridge.On("doc.dirty", payload =>
        {
            _dirty = WebBridge.Bool(payload, "dirty");
            if (_activeTab is not null)
            {
                _activeTab.Dirty = _dirty;
            }

            UpdateTitle();
        });
        _bridge.On("doc.status", UpdateStatus);
        _bridge.On("doc.text", payload =>
        {
            var reqId = WebBridge.Int(payload, "reqId");
            if (_textRequests.Remove(reqId, out var source))
            {
                source.TrySetResult(WebBridge.Str(payload, "text") ?? string.Empty);
            }
        });
        _bridge.On("ui.save", _unused => { _ = SaveAsync(); });
        _bridge.On("ui.open", _ => OpenFileFromDialog());
        _bridge.On("ui.toggleMode", _ => CycleMode());
        _bridge.On("image.paste", OnImagePaste);
        _bridge.On("link.open", payload =>
        {
            var href = WebBridge.Str(payload, "href");
            if (!string.IsNullOrWhiteSpace(href) && (href.StartsWith("http://") || href.StartsWith("https://")))
            {
                Process.Start(new ProcessStartInfo(href) { UseShellExecute = true });
            }
        });
        _bridge.On("export.html.result", payload =>
        {
            var reqId = WebBridge.Int(payload, "reqId");
            if (_htmlRequests.Remove(reqId, out var source))
            {
                var error = WebBridge.Str(payload, "error");
                if (error is null)
                {
                    source.TrySetResult(WebBridge.Str(payload, "html") ?? string.Empty);
                }
                else
                {
                    source.TrySetException(new InvalidOperationException(error));
                }
            }
        });
        _bridge.On("export.pdf.ready", payload =>
        {
            var reqId = WebBridge.Int(payload, "reqId");
            if (_pdfRequests.Remove(reqId, out var source))
            {
                source.TrySetResult(true);
            }
        });
    }

    private void SendInit()
    {
        if (_bridge is null)
        {
            return;
        }

        var settings = _settings.Settings;

        if (Tabs.Count == 0)
        {
            var tab = new DocTab
            {
                DocId = "untitled-1",
                Path = "未命名.md",
                Title = "未命名.md",
                PendingText = string.Empty,
                IsActive = true,
            };
            Tabs.Add(tab);
            _activeTab = tab;
            _docId = tab.DocId;
        }

        _bridge.Send("init", new
        {
            settings = new
            {
                theme = settings.Theme,
                editor = new
                {
                    mode = settings.Editor.Mode,
                    fontFamily = settings.Editor.FontFamily,
                    fontSize = settings.Editor.FontSize,
                    tabSize = settings.Editor.TabSize,
                    wordWrap = settings.Editor.WordWrap,
                    lineNumbers = settings.Editor.LineNumbers,
                    lintEnabled = settings.Editor.LintEnabled,
                    scrollSync = settings.Editor.ScrollSync,
                },
            },
            lintConfig = LoadLintConfig(),
            doc = BuildDocumentPayload(),
        });

        LoadingOverlay.Visibility = Visibility.Collapsed;
        UpdateTitle();

        if (App.SelfTestDirectory is { } selfTestDirectory)
        {
            _ = RunSelfTestAsync(selfTestDirectory);
        }
    }

    /// <summary>
    /// End-to-end smoke test: opens a document, reads it back from the web layer, then produces
    /// Markdown, HTML and PDF. Used by the release checklist and by CI.
    /// </summary>
    private async Task RunSelfTestAsync(string directory)
    {
        var report = new List<string>();
        var ok = true;

        try
        {
            Directory.CreateDirectory(directory);
            var sample = Path.Combine(directory, "selftest.md");
            await File.WriteAllTextAsync(sample,
                "# Self Test\n\n段落文本，用来验证往返。\n\n```csharp\nvar answer = 42;\n```\n\n" +
                "| 列 A | 列 B |\n| --- | --- |\n| 1 | 2 |\n\n> [!NOTE]\n> 自检用的提示块。\n\n$$E = mc^2$$\n\n```mermaid\ngraph LR\n  A --> B\n```\n");

            await OpenPathAsync(sample);
            await Task.Delay(1500);

            var text = await RequestTextAsync();
            report.Add($"read-back: {(text?.Length ?? 0)} chars");
            if (string.IsNullOrEmpty(text) || !text.Contains("Self Test"))
            {
                ok = false;
                report.Add("FAIL read-back");
            }

            if (_bridge is not null)
            {
                var reqId = ++_requestCounter;
                var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _htmlRequests[reqId] = source;
                _bridge.Send("export.html", new { reqId });
                var html = await source.Task.WaitAsync(TimeSpan.FromSeconds(25));
                var htmlPath = Path.Combine(directory, "selftest.html");
                await File.WriteAllTextAsync(htmlPath, html);
                report.Add($"html: {html.Length} chars");
                if (html.Length < 800 || !html.Contains("<h1"))
                {
                    ok = false;
                    report.Add("FAIL html export");
                }
            }

            var pdfPath = Path.Combine(directory, "selftest.pdf");
            var pdfOk = await WritePdfAsync(pdfPath);
            var pdfSize = File.Exists(pdfPath) ? new FileInfo(pdfPath).Length : 0;
            report.Add($"pdf: {pdfSize} bytes (ok={pdfOk})");
            if (!pdfOk || pdfSize < 1000)
            {
                ok = false;
                report.Add("FAIL pdf export");
            }

            var mdPath = Path.Combine(directory, "selftest-export.md");
            if (text is not null)
            {
                DocumentSession.SaveAs(mdPath, text, Environment.NewLine);
                report.Add($"markdown: {new FileInfo(mdPath).Length} bytes");
            }

            // multi-tab: a second document must keep its own text, and switching back must restore the first.
            var second = Path.Combine(directory, "second.md");
            await File.WriteAllTextAsync(second, "# Second\n\n第二个文档\n");
            await OpenPathAsync(second);
            await Task.Delay(1500);
            var secondText = await RequestTextAsync();
            if (secondText is null || !secondText.Contains("Second"))
            {
                await Task.Delay(1500);
                secondText = await RequestTextAsync();
            }

            report.Add($"tabs: {Tabs.Count}");
            if (Tabs.Count < 2 || secondText is null || !secondText.Contains("Second"))
            {
                ok = false;
                report.Add($"FAIL second tab ({secondText?.Length ?? -1} chars)");
            }

            var firstTab = Tabs.FirstOrDefault(tab => tab.Path.EndsWith("selftest.md", StringComparison.OrdinalIgnoreCase));
            if (firstTab is null)
            {
                ok = false;
                report.Add("FAIL tab lookup");
            }
            else
            {
                SetActiveTab(firstTab);
                await Task.Delay(900);
                var restored = await RequestTextAsync();
                if (restored is null || !restored.Contains("Self Test"))
                {
                    ok = false;
                    report.Add("FAIL tab switch back");
                }
                else
                {
                    report.Add($"tab restore: {restored.Length} chars");
                }
            }
        }
        catch (Exception ex)
        {
            ok = false;
            report.Add($"EXCEPTION {ex}");
        }

        report.Add(ok ? "RESULT PASS" : "RESULT FAIL");
        await File.WriteAllLinesAsync(Path.Combine(directory, "selftest.txt"), report);
        App.LogInfo($"self-test {(ok ? "PASS" : "FAIL")}");
        Application.Current.Shutdown(ok ? 0 : 1);
    }

    private Dictionary<string, object> BuildDocumentPayload()
    {
        var tab = _activeTab;
        if (tab?.Session is null)
        {
            _docId = tab?.DocId ?? "untitled-1";
            return new Dictionary<string, object>
            {
                ["docId"] = _docId,
                ["path"] = tab?.Path ?? "未命名.md",
                ["kind"] = "markdown",
                ["text"] = tab?.PendingText ?? string.Empty,
                ["mode"] = _settings.Settings.Editor.Mode,
            };
        }

        return new Dictionary<string, object>
        {
            ["docId"] = tab.DocId,
            ["path"] = tab.Path,
            ["kind"] = KindName(tab.Session.Kind),
            ["text"] = tab.Session.OriginalText,
            ["mode"] = _settings.Settings.Editor.Mode,
        };
    }

    private static string KindName(DocumentKind kind) => kind switch
    {
        DocumentKind.Markdown => "markdown",
        DocumentKind.Json => "json",
        DocumentKind.Xml => "xml",
        DocumentKind.Yaml => "yaml",
        _ => "plain",
    };

    private object LoadLintConfig()
    {
        var directory = _document is null ? null : Path.GetDirectoryName(_document.Path);
        if (directory is not null)
        {
            foreach (var name in new[] { ".markdownlint.json", ".markdownlint.jsonc" })
            {
                var candidate = Path.Combine(directory, name);
                if (!File.Exists(candidate))
                {
                    continue;
                }

                try
                {
                    var json = File.ReadAllText(candidate);
                    json = json.Replace("//", string.Empty);
                    using var document = JsonDocument.Parse(json);
                    return document.RootElement.Clone();
                }
                catch
                {
                    break;
                }
            }
        }

        return new Dictionary<string, object> { ["default"] = true };
    }

    // ---------- document lifecycle ----------

    private async Task OpenPathAsync(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var full = Path.GetFullPath(path);
        var existing = Tabs.FirstOrDefault(tab =>
            string.Equals(tab.Path, full, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SetActiveTab(existing);
            return;
        }

        try
        {
            if (IsProbablyBinary(full))
            {
                StatusPath.Text = $"二进制文件不支持编辑：{Path.GetFileName(full)}";
                return;
            }

            var session = DocumentSession.Open(full);
            var tab = new DocTab
            {
                DocId = session.Path,
                Path = session.Path,
                Session = session,
                Title = session.FileName,
            };

            Tabs.Add(tab);
            SetActiveTab(tab);

            RememberRecent(full);
            StatusPath.Text = session.IsLossyFallback
                ? $"{full}（编码无法识别，保存将使用当前编码）"
                : full;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"打开文件失败：\n{ex.Message}", "MarkTop",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Makes a tab current. The web layer keeps a per-docId EditorState, so its undo stack survives.</summary>
    private void SetActiveTab(DocTab tab)
    {
        foreach (var other in Tabs)
        {
            other.IsActive = ReferenceEquals(other, tab);
        }

        _activeTab = tab;
        _document = tab.Session;
        _docId = tab.DocId;
        _dirty = tab.Dirty;

        if (tab.Session is not null)
        {
            MapDocumentHost(Path.GetDirectoryName(tab.Path));
            StatusEncoding.Text = tab.Session.Encoding.DisplayName;
            StatusEol.Text = LineEndings.Describe(tab.Session.LineEnding);
        }

        _bridge?.Send("doc.open", new
        {
            docId = tab.DocId,
            path = tab.Path,
            kind = KindName(tab.Session?.Kind ?? DocumentKind.Markdown),
            text = tab.Session?.OriginalText ?? tab.PendingText ?? string.Empty,
            mode = _settings.Settings.Editor.Mode,
        });

        UpdateTitle();
    }

    private void CreateUntitledTab(string text, string suggestedName)
    {
        var tab = new DocTab
        {
            DocId = $"untitled-{++_requestCounter}",
            Path = suggestedName,
            Session = null,
            PendingText = text,
            Title = suggestedName,
        };

        Tabs.Add(tab);
        SetActiveTab(tab);
        StatusEncoding.Text = "UTF-8";
        StatusEol.Text = LineEndings.Describe(Environment.NewLine);
    }

    private async void OnTabClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DocTab tab })
        {
            SetActiveTab(tab);
        }

        await Task.CompletedTask;
    }

    private async void OnTabCloseClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: DocTab tab })
        {
            return;
        }

        if (tab.Dirty)
        {
            SetActiveTab(tab);
            if (!await ConfirmDiscardAsync())
            {
                return;
            }
        }

        var wasActive = ReferenceEquals(tab, _activeTab);
        Tabs.Remove(tab);

        if (!wasActive)
        {
            return;
        }

        if (Tabs.Count > 0)
        {
            SetActiveTab(Tabs[^1]);
        }
        else
        {
            CreateUntitledTab(string.Empty, "未命名.md");
        }
    }

    private void SendOpenDocument()
    {
        if (_bridge is null || _document is null)
        {
            return;
        }

        _bridge.Send("doc.open", new
        {
            docId = _docId,
            path = _document.Path,
            kind = KindName(_document.Kind),
            text = _document.OriginalText,
            mode = _settings.Settings.Editor.Mode,
        });
    }

    private async Task SaveAsync(bool saveAs = false)
    {
        if (_bridge is null)
        {
            return;
        }

        if (_document is null || saveAs)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Markdown (*.md)|*.md|所有文件 (*.*)|*.*",
                FileName = _document is null ? "未命名.md" : Path.GetFileName(_document.Path),
                DefaultExt = ".md",
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var text = await RequestTextAsync();
            if (text is null)
            {
                return;
            }

            try
            {
                DocumentSession.SaveAs(dialog.FileName, text, Environment.NewLine);
                _document = DocumentSession.Open(dialog.FileName);
                _docId = _document.Path;
                if (_activeTab is not null)
                {
                    _activeTab.DocId = _document.Path;
                    _activeTab.Path = _document.Path;
                    _activeTab.Session = _document;
                    _activeTab.Title = _document.FileName;
                    _activeTab.PendingText = null;
                    _activeTab.Dirty = false;
                }

                MapDocumentHost(Path.GetDirectoryName(dialog.FileName));
                SendOpenDocument();
                RememberRecent(dialog.FileName);
                StatusPath.Text = dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"保存失败：\n{ex.Message}", "MarkTop",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else
        {
            var text = await RequestTextAsync();
            if (text is null)
            {
                return;
            }

            try
            {
                _document.Save(text);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(this, "文件是只读的，请另存为。", "MarkTop",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                await SaveAsync(saveAs: true);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"保存失败：\n{ex.Message}", "MarkTop",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _dirty = false;
        if (_activeTab is not null)
        {
            _activeTab.Dirty = false;
            _activeTab.Session = _document;
        }

        _bridge.Send("doc.saved");
        UpdateTitle();
        StatusPath.Text = $"已保存 · {DateTime.Now:HH:mm:ss}";

        if (_profileRepo is { } repo && _document is not null &&
            PathGuard.IsWithin(repo.Path, _document.Path))
        {
            await PublishProfileAsync(repo);
        }
    }

    private async Task PublishProfileAsync(ProfileRepo repo)
    {
        StatusPath.Text = "正在提交并推送…";
        var message = string.IsNullOrWhiteSpace(_settings.Settings.Git.CommitMessage)
            ? GitSettings.DefaultCommitMessage
            : _settings.Settings.Git.CommitMessage;

        var result = await _profileService.PublishAsync(repo, message, _settings.Settings.Git.AutoPush);
        if (result.Success)
        {
            StatusPath.Text = result.Pushed
                ? $"已推送到 GitHub · {DateTime.Now:HH:mm:ss}"
                : result.Committed ? "已本地提交（未推送）" : "没有需要提交的改动";
            return;
        }

        StatusPath.Text = $"提交失败：{result.Error!.Message}";
        MessageBox.Show(this, $"Git 操作失败：\n\n{result.Error!.Message}", "MarkTop",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async Task<string?> RequestTextAsync()
    {
        if (_bridge is null)
        {
            return null;
        }

        var reqId = ++_requestCounter;
        var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _textRequests[reqId] = source;
        _bridge.Send("req.text", new { reqId });

        var completed = await Task.WhenAny(source.Task, Task.Delay(8000));
        if (completed != source.Task)
        {
            _textRequests.Remove(reqId);
            MessageBox.Show(this, "无法从编辑器读取内容，请重试。", "MarkTop",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        return await source.Task;
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        var answer = MessageBox.Show(this, "当前文档有未保存的修改，是否保存？", "MarkTop",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (answer == MessageBoxResult.Yes)
        {
            await SaveAsync();
            return !_dirty;
        }

        return true;
    }

    private void MapDocumentHost(string? directory)
    {
        if (EditorWebView.CoreWebView2 is null || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            EditorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "doc.local", directory, CoreWebView2HostResourceAccessKind.DenyCors);
        }
        catch
        {
            // mapping can only be refreshed once the document folder is readable
        }
    }

    // ---------- editing modes / theme ----------

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressModeEvents || _bridge is null || sender is not RadioButton { Tag: string mode })
        {
            return;
        }

        ApplyMode(mode);
    }

    private void ApplyMode(string mode)
    {
        _suppressModeEvents = true;
        ModeSplit.IsChecked = mode == "split";
        ModeSource.IsChecked = mode == "source";
        _suppressModeEvents = false;

        _bridge?.Send("mode.set", new { mode });

        _settings.Settings.Editor.Mode = mode;
        _settings.Save();
    }

    private void CycleMode()
    {
        var order = new[] { "split", "source" };
        var index = Array.IndexOf(order, _settings.Settings.Editor.Mode);
        ApplyMode(order[(index + 1) % order.Length]);
    }

    private void OnToggleThemeClick(object sender, RoutedEventArgs e)
    {
        var next = _settings.Settings.Theme == "dark" ? "light" : "dark";
        _settings.Settings.Theme = next;
        _settings.Save();
        ApplyTheme(next);
        _bridge?.Send("theme.set", new { theme = next });
    }

    private void ApplyTheme(string theme)
    {
        var source = theme == "light" ? "Theme/Palette.Light.xaml" : "Theme/Palette.Dark.xaml";
        var dictionary = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
        if (Application.Current.Resources.MergedDictionaries.Count > 0)
        {
            Application.Current.Resources.MergedDictionaries[0] = dictionary;
        }
        else
        {
            Application.Current.Resources.MergedDictionaries.Add(dictionary);
        }

        ThemeButton.Content = theme == "light" ? "☀" : "☾";
        EditorWebView.DefaultBackgroundColor = theme == "light"
            ? System.Drawing.Color.White
            : System.Drawing.Color.FromArgb(0x1E, 0x1E, 0x1E);
    }

    private void RestoreWindowState()
    {
        var window = _settings.Settings.Window;
        if (window.Width > 200 && window.Height > 200)
        {
            Width = window.Width;
            Height = window.Height;
        }

        if (window.Left is { } left && window.Top is { } top && left > -10000 && top > -10000)
        {
            Left = left;
            Top = top;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        if (window.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    // ---------- files & workspace ----------

    private void OpenFileFromDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "文档 (*.md;*.markdown;*.json;*.xml;*.yml;*.yaml;*.lang)|*.md;*.markdown;*.json;*.xml;*.yml;*.yaml;*.lang|所有文件 (*.*)|*.*",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _ = OpenPathAsync(dialog.FileName);
        }
    }

    private void OnOpenFileClick(object sender, RoutedEventArgs e) => OpenFileFromDialog();

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择工作区文件夹" };
        if (dialog.ShowDialog(this) == true)
        {
            LoadWorkspace(dialog.FolderName);
        }
    }

    private void LoadWorkspace(string directory)
    {
        _workspace = directory;
        WorkspaceLabel.Text = directory;
        FileTree.ItemsSource = new ObservableCollection<FileNode> { BuildDirectoryNode(directory) };
        _settings.Settings.LastWorkspace = directory;
        _settings.Save();
    }

    private static FileNode BuildDirectoryNode(string path)
    {
        var node = new FileNode
        {
            Name = Path.GetFileName(path) is { Length: > 0 } name ? name : path,
            FullPath = path,
            IsDirectory = true,
            Glyph = "▸",
        };

        node.Children.Add(new FileNode { Name = "…", IsPlaceholder = true });
        return node;
    }

    private void PopulateDirectory(FileNode node)
    {
        node.Children.Clear();
        IEnumerable<string> directories;
        IEnumerable<string> files;
        try
        {
            directories = Directory.EnumerateDirectories(node.FullPath)
                .Where(directory => !new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.Hidden))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            files = Directory.EnumerateFiles(node.FullPath)
                .Where(file => !new FileInfo(file).Attributes.HasFlag(FileAttributes.Hidden))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return;
        }

        foreach (var directory in directories)
        {
            node.Children.Add(BuildDirectoryNode(directory));
        }

        foreach (var file in files)
        {
            node.Children.Add(new FileNode
            {
                Name = Path.GetFileName(file),
                FullPath = file,
                IsDirectory = false,
                Glyph = "·",
            });
        }
    }

    private void OnFileTreeExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: FileNode node } item && node.IsDirectory)
        {
            if (node.Children.Count == 1 && node.Children[0].IsPlaceholder)
            {
                PopulateDirectory(node);
            }

            item.IsExpanded = true;
        }
    }

    private void OnFileTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileTree.SelectedItem is FileNode { IsDirectory: false } node)
        {
            _ = OpenPathAsync(node.FullPath);
        }
    }

    private void RememberRecent(string path)
    {
        var recent = _settings.Settings.RecentFiles;
        recent.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, path);
        while (recent.Count > 20)
        {
            recent.RemoveAt(recent.Count - 1);
        }

        _settings.Save();
    }

    // ---------- export ----------

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu is not null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    private async void OnExportMarkdownClick(object sender, RoutedEventArgs e)
    {
        var text = await RequestTextAsync();
        if (text is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Markdown (*.md)|*.md",
            FileName = _document is null ? "未命名.md" : Path.GetFileNameWithoutExtension(_document.Path) + ".md",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var lineEnding = _document?.LineEnding ?? Environment.NewLine;
            DocumentSession.SaveAs(dialog.FileName, text, lineEnding);
            StatusPath.Text = $"已导出 Markdown：{dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导出失败：\n{ex.Message}", "MarkTop", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnExportHtmlClick(object sender, RoutedEventArgs e)
    {
        if (_bridge is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "HTML (*.html)|*.html",
            FileName = (_document is null ? "未命名" : Path.GetFileNameWithoutExtension(_document.Path)) + ".html",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var reqId = ++_requestCounter;
            var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _htmlRequests[reqId] = source;
            _bridge.Send("export.html", new { reqId });

            var completed = await Task.WhenAny(source.Task, Task.Delay(15000));
            if (completed != source.Task)
            {
                _htmlRequests.Remove(reqId);
                throw new TimeoutException("生成 HTML 超时。");
            }

            var html = await source.Task;
            await File.WriteAllTextAsync(dialog.FileName, html);
            StatusPath.Text = $"已导出 HTML：{dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导出失败：\n{ex.Message}", "MarkTop", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnExportPdfClick(object sender, RoutedEventArgs e)
    {
        if (_bridge is null || EditorWebView.CoreWebView2 is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = (_document is null ? "未命名" : Path.GetFileNameWithoutExtension(_document.Path)) + ".pdf",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            StatusPath.Text = "正在生成 PDF…";
            var ok = await WritePdfAsync(dialog.FileName);
            StatusPath.Text = ok ? $"已导出 PDF：{dialog.FileName}" : "PDF 导出未完成。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导出失败：\n{ex.Message}", "MarkTop", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task<bool> WritePdfAsync(string path)
    {
        if (_bridge is null || EditorWebView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("编辑器尚未就绪。");
        }

        var reqId = ++_requestCounter;
        var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pdfRequests[reqId] = source;
        _bridge.Send("export.pdf", new { reqId });

        try
        {
            await source.Task.WaitAsync(TimeSpan.FromSeconds(20));

            var core = EditorWebView.CoreWebView2;
            var print = core.Environment.CreatePrintSettings();
            const double a4Width = 8.27;
            const double a4Height = 11.69;
            var margin = Math.Max(0, _settings.Settings.Export.PdfMarginMm) / 25.4;
            print.PageWidth = a4Width;
            print.PageHeight = a4Height;
            print.MarginTop = margin;
            print.MarginBottom = margin;
            print.MarginLeft = margin;
            print.MarginRight = margin;
            print.ShouldPrintBackgrounds = true;
            print.ShouldPrintHeaderAndFooter = false;
            print.Orientation = CoreWebView2PrintOrientation.Portrait;

            await Task.Delay(300);
            return await core.PrintToPdfAsync(path, print);
        }
        finally
        {
            _pdfRequests.Remove(reqId);
            _bridge.Send("print.hide");
        }
    }

    private async void OnImportHtmlClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "HTML (*.html;*.htm)|*.html;*.htm|所有文件 (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var html = await File.ReadAllTextAsync(dialog.FileName);
            var markdown = Core.Export.HtmlImporter.ToMarkdown(html);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                MessageBox.Show(this, "没有从该 HTML 中提取到可转换的内容。", "MarkTop",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_dirty && !await ConfirmDiscardAsync())
            {
                return;
            }

            OpenTextAsUntitled(markdown, Path.GetFileNameWithoutExtension(dialog.FileName) + ".md");
            StatusPath.Text = $"已从 HTML 导入（{Path.GetFileName(dialog.FileName)}），保存时会提示新路径";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导入失败：\n{ex.Message}", "MarkTop",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Opens in-memory content as an untitled document; the first save asks for a path.</summary>
    private void OpenTextAsUntitled(string text, string suggestedName)
    {
        if (_bridge is null)
        {
            return;
        }

        CreateUntitledTab(text, suggestedName);
    }

    // ---------- image paste ----------

    private void OnImagePaste(JsonElement payload)
    {
        var reqId = WebBridge.Int(payload, "reqId");
        var name = WebBridge.Str(payload, "name") ?? "image.png";
        var base64 = WebBridge.Str(payload, "base64");

        if (_bridge is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(base64) || _document is null)
        {
            _bridge.Send("image.saved", new { reqId, success = false, error = "请先保存文档，再粘贴图片。" });
            return;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            var relative = AssetStore.SaveImage(_document.Path, name, bytes);
            _bridge.Send("image.saved", new { reqId, success = true, relativePath = relative });
        }
        catch (Exception ex)
        {
            _bridge.Send("image.saved", new { reqId, success = false, error = ex.Message });
        }
    }

    // ---------- git profile readme ----------

    private async void OnGitReadmeClick(object sender, RoutedEventArgs e)
    {
        var dialog = new GitReadmeDialog(_settings.Settings.Git.UserName, _settings.Settings.Git.CommitMessage)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _settings.Settings.Git.UserName = dialog.UserName;
        _settings.Settings.Git.CommitMessage = dialog.CommitMessage;
        _settings.Save();

        var progress = new Progress<string>(message => StatusPath.Text = message);
        StatusPath.Text = "正在准备个人主页仓库…";

        try
        {
            var verify = await _git.VerifyAsync();
            if (!verify.Ok)
            {
                MessageBox.Show(this, verify.Error, "MarkTop", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = await _profileService.PrepareAsync(dialog.UserName, progress);
            if (!result.Success || result.Repo is null)
            {
                MessageBox.Show(this, result.Error?.Message ?? "准备仓库失败。", "MarkTop",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _profileRepo = result.Repo;
            await OpenPathAsync(result.Repo.ReadmePath);

            StatusPath.Text = result.Warnings.Count == 0
                ? $"已打开 {result.Repo.ReadmePath}（保存后自动提交）"
                : string.Join(" · ", result.Warnings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"准备仓库失败：\n{ex.Message}", "MarkTop",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- misc ----------

    private void UpdateStatus(JsonElement payload)
    {
        StatusCursor.Text = $"Ln {WebBridge.Int(payload, "line")}, Col {WebBridge.Int(payload, "column")}";
        StatusCounts.Text = $"{WebBridge.Int(payload, "words")} 词 · {WebBridge.Int(payload, "chars")} 字符";
    }

    private void UpdateTitle()
    {
        var name = _activeTab?.Title ?? "未命名";
        Title = $"{name}{(_dirty ? " •" : string.Empty)} — MarkTop";
        if (_activeTab?.Session is not null)
        {
            StatusPath.Text = _activeTab.Path;
        }
    }

    private static bool IsProbablyBinary(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[Math.Min(4096, (int)Math.Min(stream.Length, 4096))];
            var read = stream.Read(buffer, 0, buffer.Length);
            return buffer.AsSpan(0, read).Contains((byte)0);
        }
        catch
        {
            return false;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mod = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (mod && e.Key == Key.S)
        {
            e.Handled = true;
            _ = SaveAsync();
        }
        else if (mod && e.Key == Key.O)
        {
            e.Handled = true;
            OpenFileFromDialog();
        }
        else if (mod && e.Key == Key.E)
        {
            e.Handled = true;
            CycleMode();
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => _ = SaveAsync();

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var window = _settings.Settings.Window;
        window.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            window.Width = Width;
            window.Height = Height;
            window.Left = Left;
            window.Top = Top;
        }

        _settings.Save();

        if (Tabs.Any(tab => tab.Dirty))
        {
            var answer = MessageBox.Show(this, "有文档存在未保存的修改，确定要退出吗？", "MarkTop",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
            }
        }
    }
}

/// <summary>One open document. The web layer keeps the text; this object only tracks identity and dirty state.</summary>
public sealed class DocTab : System.ComponentModel.INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _path = string.Empty;
    private bool _dirty;
    private bool _isActive;
    private MarkTop.Core.Documents.DocumentSession? _session;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string DocId { get; set; } = string.Empty;

    /// <summary>Content for documents that do not exist on disk yet (imported HTML, new file).</summary>
    public string? PendingText { get; set; }

    public MarkTop.Core.Documents.DocumentSession? Session
    {
        get => _session;
        set => Set(ref _session, value);
    }

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public string Path
    {
        get => _path;
        set => Set(ref _path, value);
    }

    public bool Dirty
    {
        get => _dirty;
        set => Set(ref _dirty, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}

public sealed class FileNode
{
    public string Name { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }

    public bool IsPlaceholder { get; set; }

    public string Glyph { get; set; } = "·";

    public ObservableCollection<FileNode> Children { get; } = new();
}
