import { connect, isHostAvailable, on, post } from "./bridge";
import { Editor, type DocKind } from "./editor";
import { Preview } from "./preview";
import { enhanceGithubSyntax } from "./github";
import { ScrollSync } from "./scrollSync";
import { setLintConfig } from "./lint";
import { renderForExport } from "./export";
import type { EditorState } from "@codemirror/state";

type Mode = "split" | "source";

interface EditorSettings {
  mode?: string;
  fontFamily?: string;
  fontSize?: number;
  tabSize?: number;
  wordWrap?: boolean;
  lineNumbers?: boolean;
  lintEnabled?: boolean;
  scrollSync?: boolean;
}

interface InitSettings {
  theme?: string;
  editor?: EditorSettings;
}

interface DocPayload {
  docId: string;
  path: string;
  kind: string;
  text: string;
  mode?: string;
}

const workspace = document.getElementById("workspace") as HTMLElement;
const cmHost = document.getElementById("cm-host") as HTMLElement;
const previewElement = document.getElementById("preview") as HTMLElement;
const printRoot = document.getElementById("print-root") as HTMLElement;
const toast = document.getElementById("toast") as HTMLElement;

const preview = new Preview(previewElement);
let editor: Editor | null = null;
let scrollSync: ScrollSync | null = null;

let settings: InitSettings = { theme: "dark", editor: {} };
let currentDocId = "";
let currentPath = "";
let currentKind: DocKind = "markdown";
let currentMode: Mode = "split";
let suppressDirty = false;
const savedStates = new Map<string, { state: EditorState; kind: DocKind; path: string }>();
const pendingImages = new Map<number, (relativePath: string) => void>();
let requestCounter = 1;
let renderTimer = 0;
let dirtyTimer = 0;

function nextRequestId(): number {
  return requestCounter++;
}

function isDark(): boolean {
  return (settings.theme ?? "dark") === "dark";
}

function kindFrom(value: string): DocKind {
  switch (value) {
    case "json":
    case "xml":
    case "yaml":
    case "markdown":
      return value;
    default:
      return "plain";
  }
}

function showToast(message: string, error = false): void {
  toast.textContent = message;
  toast.hidden = false;
  toast.classList.toggle("error", error);
  window.setTimeout(() => {
    toast.hidden = true;
  }, 3200);
}

function getText(): string {
  return editor?.getText() ?? "";
}

function countWords(text: string): number {
  const cjk = (text.match(/[\u3400-\u4dbf\u4e00-\u9fff\u3040-\u30ff\uac00-\ud7af]/g) ?? []).length;
  const latin = (text.replace(/[\u3400-\u4dbf\u4e00-\u9fff\u3040-\u30ff\uac00-\ud7af]/g, " ").match(/[A-Za-z0-9_'-]+/g) ?? []).length;
  return cjk + latin;
}

function reportStatus(line = 1, column = 1, selectedChars = 0): void {
  const text = editor?.getText() ?? "";
  post({
    type: "doc.status",
    docId: currentDocId,
    line,
    column,
    selectedChars,
    chars: text.length,
    lines: text.length === 0 ? 1 : text.split("\n").length,
    words: countWords(text),
  });
}

function handleChange(text: string): void {
  if (suppressDirty) {
    return;
  }

  post({ type: "doc.dirty", docId: currentDocId, dirty: true, chars: text.length });
  window.clearTimeout(dirtyTimer);
  dirtyTimer = window.setTimeout(() => reportStatus(), 250);

  if (currentKind === "markdown" && currentMode === "split") {
    window.clearTimeout(renderTimer);
    renderTimer = window.setTimeout(() => {
      void renderPreview();
    }, 320);
  }
}

async function renderPreview(): Promise<void> {
  if (currentKind !== "markdown") {
    return;
  }

  const text = getText();
  await preview.render(text);
  enhanceGithubSyntax(previewElement);
  scrollSync?.scheduleRebuild(text);
}

function applyTheme(): void {
  const dark = isDark();
  document.documentElement.dataset.theme = dark ? "dark" : "light";
  preview.setTheme(dark);
  editor?.setTheme(dark);
}

function applyMode(mode: Mode): void {
  const effective: Mode = currentKind === "markdown" ? mode : "source";
  currentMode = effective;
  workspace.classList.remove("mode-split", "mode-source");
  workspace.classList.add(`mode-${effective}`);

  if (effective === "split") {
    void renderPreview();
  }

  editor?.focus();
}

function ensureEditor(payload: DocPayload): void {
  if (editor) {
    return;
  }

  editor = new Editor(
    cmHost,
    {
      text: payload.text,
      kind: kindFrom(payload.kind),
      dark: isDark(),
      wordWrap: settings.editor?.wordWrap ?? true,
      lineNumbers: settings.editor?.lineNumbers ?? true,
      fontSize: settings.editor?.fontSize ?? 14,
      tabSize: settings.editor?.tabSize ?? 4,
    },
    {
      onChange: handleChange,
      onSelection: (line, column, selectedChars) => reportStatus(line, column, selectedChars),
      onSave: () => post({ type: "ui.save" }),
      onImagePaste: (name, base64, insert) => {
        const reqId = nextRequestId();
        pendingImages.set(reqId, insert);
        post({ type: "image.paste", reqId, docId: currentDocId, name, base64 });
      },
    },
  );

  scrollSync = new ScrollSync(() => editor?.view ?? null, previewElement);
  scrollSync.attach();
}

function openDocument(payload: DocPayload): void {
  if (currentDocId && editor) {
    savedStates.set(currentDocId, { state: editor.view.state, kind: currentKind, path: currentPath });
  }

  ensureEditor(payload);
  currentDocId = payload.docId;
  currentPath = payload.path;
  currentKind = kindFrom(payload.kind);
  workspace.classList.toggle("kind-code", currentKind !== "markdown");

  const saved = savedStates.get(payload.docId);
  if (saved) {
    suppressDirty = true;
    editor!.view.setState(saved.state);
    editor!.setKind(saved.kind);
    suppressDirty = false;
  } else {
    suppressDirty = true;
    editor!.setText(payload.text);
    editor!.setKind(currentKind);
    suppressDirty = false;
  }

  applyMode((payload.mode as Mode) ?? (settings.editor?.mode as Mode) ?? "split");
  reportStatus();
}

const LINK_BASE = location.hostname === "app.local" ? "https://doc.local/" : "";

/** Inlines KaTeX woff2 fonts so an exported HTML file keeps its math typography offline. */
async function inlineKatexFonts(css: string): Promise<string> {
  const pattern = /url\(fonts\/([^)]+?\.woff2)\)/g;
  const names = new Set<string>();
  for (const match of css.matchAll(pattern)) {
    names.add(match[1]);
  }

  let result = css;
  for (const name of names) {
    try {
      const response = await fetch(`vendor/vditor/dist/js/katex/fonts/${name}`);
      if (!response.ok) {
        continue;
      }

      const buffer = await response.arrayBuffer();
      let binary = "";
      const bytes = new Uint8Array(buffer);
      const chunk = 0x8000;
      for (let index = 0; index < bytes.length; index += chunk) {
        binary += String.fromCharCode.apply(null, Array.from(bytes.subarray(index, index + chunk)));
      }

      const base64 = btoa(binary);
      result = result.split(`url(fonts/${name})`).join(`url(data:font/woff2;base64,${base64})`);
    } catch {
      // keep the original reference if the font cannot be read
    }
  }

  return result;
}

function buildStandaloneHtml(title: string): Promise<string> {
  return (async () => {
    const body = await renderForExport(getText(), { dark: false, linkBase: LINK_BASE });
    const files = [
      "vendor/vditor/dist/index.css",
      "vendor/vditor/dist/css/content-theme/light.css",
      "vendor/vditor/dist/js/highlight.js/styles/github.min.css",
      "tokens.css",
      "app.css",
      "print.css",
    ];

    const styles: string[] = [];
    for (const file of files) {
      try {
        const response = await fetch(file);
        if (response.ok) {
          styles.push(await response.text());
        }
      } catch {
        // a missing optional stylesheet must not break the export
      }
    }

    try {
      const response = await fetch("vendor/vditor/dist/js/katex/katex.min.css");
      if (response.ok) {
        styles.push(await inlineKatexFonts(await response.text()));
      }
    } catch {
      // math falls back to inline styles
    }

    const safeTitle = title.replace(/[<>&"]/g, (c) => ({ "<": "&lt;", ">": "&gt;", "&": "&amp;", '"': "&quot;" }[c] ?? c));
    return `<!doctype html>
<html lang="zh-CN" data-theme="light">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>${safeTitle}</title>
<style>${styles.join("\n")}
.print-document { max-width: 860px; margin: 0 auto; padding: 40px 24px 80px; }
.print-document .language-mermaid svg { max-width: 100%; height: auto; }
</style>
</head>
<body class="marktop-export">
<article class="print-document vditor-reset">${body}</article>
</body>
</html>`;
  })();
}

on("init", (message) => {
  settings = (message.settings as InitSettings) ?? { theme: "dark", editor: {} };
  document.documentElement.dataset.theme = isDark() ? "dark" : "light";
  if (message.lintConfig) {
    setLintConfig(message.lintConfig);
  }

  const doc = message.doc as DocPayload | undefined;
  if (doc) {
    openDocument(doc);
  }
});

on("doc.open", (message) => {
  openDocument(message as unknown as DocPayload);
});

on("doc.setText", (message) => {
  const text = String(message.text ?? "");
  suppressDirty = true;
  editor?.setText(text);
  if (currentMode === "split") {
    void renderPreview();
  }
  suppressDirty = false;
});

on("req.text", (message) => {
  post({ type: "doc.text", reqId: message.reqId, text: getText() });
});

on("mode.set", (message) => {
  applyMode((message.mode as Mode) ?? "split");
});

on("theme.set", (message) => {
  settings.theme = String(message.theme ?? "dark");
  applyTheme();
});

on("settings.update", (message) => {
  const incoming = (message.settings as InitSettings) ?? settings;
  settings = { ...settings, ...incoming, editor: { ...settings.editor, ...incoming.editor } };
  editor?.setFontSize(settings.editor?.fontSize ?? 14);
  editor?.setWordWrap(settings.editor?.wordWrap ?? true);
  editor?.setLineNumbers(settings.editor?.lineNumbers ?? true);
});

on("lint.config", (message) => {
  setLintConfig(message.config);
});

on("image.saved", (message) => {
  const reqId = Number(message.reqId);
  const insert = pendingImages.get(reqId);
  pendingImages.delete(reqId);
  if (!insert) {
    return;
  }

  if (message.success && typeof message.relativePath === "string") {
    insert(message.relativePath);
  } else {
    showToast(String(message.error ?? "图片保存失败。"), true);
  }
});

on("export.html", (message) => {
  const reqId = Number(message.reqId);
  void buildStandaloneHtml(currentPath ? currentPath.split(/[\\/]/).pop() ?? "MarkTop" : "MarkTop")
    .then((html) => post({ type: "export.html.result", reqId, html }))
    .catch((error) => post({ type: "export.html.result", reqId, error: String(error) }));
});

on("export.pdf", (message) => {
  const reqId = Number(message.reqId);
  void (async () => {
    const body = await renderForExport(getText(), { dark: false, linkBase: LINK_BASE });
    printRoot.innerHTML = `<article class="print-document vditor-reset">${body}</article>`;
    document.documentElement.classList.add("printing");
    post({ type: "export.pdf.ready", reqId });
  })();
});

on("print.hide", () => {
  document.documentElement.classList.remove("printing");
  printRoot.innerHTML = "";
});

on("doc.saved", () => {
  post({ type: "doc.dirty", docId: currentDocId, dirty: false });
});

on("focus.editor", () => {
  editor?.focus();
});

document.addEventListener("keydown", (event) => {
  const mod = event.ctrlKey || event.metaKey;
  if (mod && event.key.toLowerCase() === "s") {
    event.preventDefault();
    post({ type: "ui.save" });
  } else if (mod && event.key.toLowerCase() === "o") {
    event.preventDefault();
    post({ type: "ui.open" });
  } else if (mod && event.key.toLowerCase() === "e") {
    event.preventDefault();
    post({ type: "ui.toggleMode" });
  }
});

previewElement.addEventListener("click", (event) => {
  const target = (event.target as HTMLElement).closest("a");
  if (target) {
    event.preventDefault();
    post({ type: "link.open", href: (target as HTMLAnchorElement).href });
  }
});

window.addEventListener("error", (event) => {
  showToast(`发生错误：${event.message}`, true);
});

window.addEventListener("unhandledrejection", (event) => {
  showToast(`发生错误：${String(event.reason)}`, true);
});

connect();

// Standalone preview mode: opening dist/index.html in a normal browser renders a demo document,
// which is how the web layer is smoke-tested without the WPF host.
if (!isHostAvailable()) {
  const demo = `# MarkTop

本地化 Markdown 编辑器 · 渲染自检

## 特性

- 双栏编辑与源码模式
- GitHub Alerts 与脚注
- 语法高亮与 \`行内代码\`
- 导出 Markdown / HTML / PDF

## 代码

\`\`\`csharp
public static class Hello
{
    public static void Main() => Console.WriteLine("Hello MarkTop");
}
\`\`\`

## 表格

| 能力 | 状态 |
| --- | --- |
| 预览 | 可用 |
| 语法检查 | 可用 |

## Alerts

> [!NOTE]
> 这是 GitHub 风格的提示块。

> [!WARNING]
> 保存前请确认路径。

## 公式

$$E = mc^2$$

## 图表

\`\`\`mermaid
graph LR
  A[源码] --> B[Lute]
  B --> C[预览]
\`\`\`

> 如果这一页排版正常，说明离线渲染链路已经通了。
`;

  openDocument({ docId: "demo", path: "README.md", kind: "markdown", text: demo, mode: "split" });
}

