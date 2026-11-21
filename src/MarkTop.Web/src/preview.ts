declare const Vditor: {
  preview: (element: HTMLElement, markdown: string, options?: Record<string, unknown>) => Promise<void>;
  md2html: (markdown: string, options?: Record<string, unknown>) => Promise<string>;
  mathRender: (element: HTMLElement | Document, options?: { math?: Record<string, unknown> }) => void;
  setContentTheme: (theme: string, path: string) => void;
};

const HOSTED = location.hostname === "app.local";
const CDN = HOSTED ? "https://app.local/vendor/vditor" : "vendor/vditor";
const CONTENT_THEME_PATH = HOSTED
  ? "https://app.local/vendor/vditor/dist/css/content-theme"
  : "vendor/vditor/dist/css/content-theme";
const LINK_BASE = HOSTED ? "https://doc.local/" : "";

export function vditorOptions(dark: boolean, extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    cdn: CDN,
    theme: { current: dark ? "dark" : "light", path: CONTENT_THEME_PATH },
    hljs: { enable: true, style: dark ? "atom-one-dark" : "github", lineNumber: false },
    math: { engine: "KaTeX", inlineDigit: false },
    markdown: {
      linkBase: LINK_BASE,
      mermaid: true,
      toc: false,
      autoSpace: true,
    },
    anchor: 0,
    lazyLoadImage: false,
    ...extra,
  };
}

export class Preview {
  private renderToken = 0;
  private dark = true;

  constructor(private readonly element: HTMLElement) {}

  setTheme(dark: boolean): void {
    this.dark = dark;
  }

  /** Renders markdown into the preview pane. Stale renders are discarded. */
  async render(markdown: string): Promise<void> {
    const token = ++this.renderToken;
    try {
      await Vditor.preview(this.element, markdown, vditorOptions(this.dark, { mode: this.dark ? "dark" : "light" }));
      this.renderMath();
    } catch (error) {
      if (token === this.renderToken) {
        this.element.innerHTML = `<pre class="marktop-error">预览渲染失败：${String(error)}</pre>`;
      }
      return;
    }
  }

  /**
   * Vditor.preview renders before its KaTeX asset finishes loading, so math blocks stay raw.
   * KaTeX is loaded eagerly in index.html; render any leftover blocks ourselves.
   */
  private renderMath(): void {
    const katex = (window as unknown as {
      katex?: { render: (tex: string, element: HTMLElement, options?: Record<string, unknown>) => void };
    }).katex;

    if (!katex) {
      return;
    }

    const blocks = Array.from(this.element.querySelectorAll<HTMLElement>(".language-math"));
    for (const block of blocks) {
      const tex = block.textContent ?? "";
      if (!tex.trim() || block.querySelector(".katex")) {
        continue;
      }

      const inline = block.classList.contains("language-math-inline") || block.tagName === "SPAN";
      try {
        katex.render(tex, block, { displayMode: !inline, throwOnError: false, output: "html" });
      } catch {
        // leave the raw TeX visible when KaTeX rejects it
      }
    }
  }

  async toHtml(markdown: string): Promise<string> {
    return Vditor.md2html(markdown, vditorOptions(this.dark));
  }

  element_(): HTMLElement {
    return this.element;
  }
}
