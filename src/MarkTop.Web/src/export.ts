declare const Vditor: {
  md2html: (markdown: string, options?: Record<string, unknown>) => Promise<string>;
};

export interface ExportOptions {
  dark: boolean;
  linkBase: string;
}

/**
 * Turns Markdown into HTML that can stand on its own: KaTeX output is baked in, Mermaid blocks become
 * inline SVG and code blocks are highlighted, because md2html alone leaves all three as raw text.
 */
export async function renderForExport(markdown: string, options: ExportOptions): Promise<string> {
  const raw = await Vditor.md2html(markdown, {
    mode: options.dark ? "dark" : "light",
    markdown: { linkBase: options.linkBase, mermaid: true, toc: false, autoSpace: true },
    math: { engine: "KaTeX", inlineDigit: false },
  });

  const container = document.createElement("div");
  container.innerHTML = raw;

  renderMath(container);
  await renderMermaid(container, options.dark);
  highlightCode(container);

  return container.innerHTML;
}

function renderMath(container: HTMLElement): void {
  const katex = (window as unknown as {
    katex?: {
      renderToString: (tex: string, options?: Record<string, unknown>) => string;
    };
  }).katex;

  if (!katex) {
    return;
  }

  for (const block of Array.from(container.querySelectorAll<HTMLElement>(".language-math"))) {
    const tex = block.textContent ?? "";
    if (!tex.trim()) {
      continue;
    }

    const inline = block.classList.contains("language-math-inline") || block.tagName === "SPAN";
    try {
      block.innerHTML = katex.renderToString(tex, {
        displayMode: !inline,
        throwOnError: false,
        output: "html",
      });
    } catch {
      // keep the raw TeX when it cannot be parsed
    }
  }
}

async function renderMermaid(container: HTMLElement, dark: boolean): Promise<void> {
  const blocks = Array.from(container.querySelectorAll<HTMLElement>(".language-mermaid"));
  if (blocks.length === 0) {
    return;
  }

  const mermaid = await ensureMermaid();
  if (!mermaid) {
    return;
  }

  mermaid.initialize({ startOnLoad: false, theme: dark ? "dark" : "default", securityLevel: "loose" });

  let index = 0;
  for (const block of blocks) {
    const code = block.textContent ?? "";
    if (!code.trim()) {
      continue;
    }

    try {
      const result = await mermaid.render(`marktop-export-${++index}`, code);
      block.innerHTML = result.svg;
    } catch {
      // leave the source visible if the diagram is invalid
    }
  }
}

interface MermaidApi {
  initialize: (options: Record<string, unknown>) => void;
  render: (id: string, code: string) => Promise<{ svg: string }>;
}

/** Vditor loads Mermaid lazily; an export in IR mode may never have triggered it. */
async function ensureMermaid(): Promise<MermaidApi | null> {
  const existing = (window as unknown as { mermaid?: MermaidApi }).mermaid;
  if (existing) {
    return existing;
  }

  try {
    await new Promise<void>((resolve, reject) => {
      const script = document.createElement("script");
      script.src = "vendor/vditor/dist/js/mermaid/mermaid.min.js";
      script.onload = () => resolve();
      script.onerror = () => reject(new Error("mermaid load failed"));
      document.head.appendChild(script);
    });
  } catch {
    return null;
  }

  return (window as unknown as { mermaid?: MermaidApi }).mermaid ?? null;
}

function highlightCode(container: HTMLElement): void {
  const hljs = (window as unknown as {
    hljs?: { highlightElement: (element: HTMLElement) => void };
  }).hljs;

  if (!hljs) {
    return;
  }

  for (const code of Array.from(container.querySelectorAll<HTMLElement>("pre code"))) {
    if (code.classList.contains("language-mermaid") || code.classList.contains("language-math")) {
      continue;
    }

    try {
      hljs.highlightElement(code);
    } catch {
      // unsupported language: render as plain text
    }
  }
}
