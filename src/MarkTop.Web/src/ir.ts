import { vditorOptions } from "./preview";

declare const Vditor: new (element: HTMLElement, options: Record<string, unknown>) => {
  getValue: () => string;
  destroy: () => void;
  focus: () => void;
  setValue: (value: string, clearStack?: boolean) => void;
  disabled: () => void;
  enable: () => void;
};

const TOOLBAR = [
  "headings",
  "bold",
  "italic",
  "strike",
  "|",
  "list",
  "ordered-list",
  "check",
  "|",
  "quote",
  "line",
  "code",
  "inline-code",
  "link",
  "table",
  "|",
  "undo",
  "redo",
  "|",
  "fullscreen",
];

/** Vditor 4.0.1 has no runtime mode switch, so the IR instance is created and destroyed on demand. */
export class IrEditor {
  private instance: ReturnType<typeof Vditor> | null = null;
  private dark: boolean;

  constructor(
    private readonly container: HTMLElement,
    private readonly onChange: (value: string) => void,
    dark: boolean,
  ) {
    this.dark = dark;
  }

  get mounted(): boolean {
    return this.instance !== null;
  }

  async mount(markdown: string): Promise<void> {
    await this.destroy();
    await new Promise<void>((resolve) => {
      this.instance = new Vditor(this.container, vditorOptions(this.dark, {
        mode: "ir",
        value: markdown,
        height: "100%",
        cache: { enable: false },
        toolbar: TOOLBAR,
        toolbarConfig: { pin: true },
        counter: { enable: false },
        outline: { enable: false, position: "left" },
        preview: {
          math: { engine: "KaTeX", inlineDigit: false },
          markdown: { mermaid: true },
          hljs: { enable: true, style: this.dark ? "atom-one-dark" : "github", lineNumber: false },
        },
        input: (value: string) => this.onChange(value),
        after: () => resolve(),
      })) as ReturnType<typeof Vditor>;
    });
  }

  getValue(): string {
    return this.instance?.getValue() ?? "";
  }

  focus(): void {
    this.instance?.focus();
  }

  async destroy(): Promise<void> {
    if (!this.instance) {
      return;
    }

    try {
      this.instance.destroy();
    } catch {
      // Vditor can throw while tearing down a partially initialised instance
    }

    this.instance = null;
    this.container.innerHTML = "";
  }
}
