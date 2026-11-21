import { Compartment, EditorState, type Extension } from "@codemirror/state";
import {
  EditorView,
  keymap,
  lineNumbers,
  highlightActiveLine,
  highlightActiveLineGutter,
  drawSelection,
  dropCursor,
  rectangularSelection,
  highlightSpecialChars,
  placeholder,
} from "@codemirror/view";
import { defaultKeymap, history, historyKeymap, indentWithTab } from "@codemirror/commands";
import {
  bracketMatching,
  foldGutter,
  foldKeymap,
  indentOnInput,
  indentUnit,
} from "@codemirror/language";
import { highlightSelectionMatches, searchKeymap } from "@codemirror/search";
import { forceLinting, lintGutter, linter, type Diagnostic } from "@codemirror/lint";
import { markdown } from "@codemirror/lang-markdown";
import { json } from "@codemirror/lang-json";
import { xml } from "@codemirror/lang-xml";
import { yaml } from "@codemirror/lang-yaml";
import { editorTheme } from "./themes";
import { requestLint } from "./lint";

export type DocKind = "markdown" | "json" | "xml" | "yaml" | "plain";

export interface EditorCallbacks {
  onChange: (text: string) => void;
  onSelection: (line: number, column: number, selectedChars: number) => void;
  onSave: () => void;
  onImagePaste: (name: string, base64: string, insert: (relativePath: string) => void) => void;
}

function languageExtension(kind: DocKind): Extension {
  switch (kind) {
    case "json":
      return json();
    case "xml":
      return xml();
    case "yaml":
      return yaml();
    case "markdown":
      return markdown();
    default:
      return [];
  }
}

export class Editor {
  readonly view: EditorView;
  private readonly languageConf = new Compartment();
  private readonly themeConf = new Compartment();
  private readonly wrapConf = new Compartment();
  private readonly gutterConf = new Compartment();
  private readonly callbacks: EditorCallbacks;
  private kind: DocKind;
  private dark: boolean;

  constructor(
    parent: HTMLElement,
    options: {
      text: string;
      kind: DocKind;
      dark: boolean;
      wordWrap: boolean;
      lineNumbers: boolean;
      fontSize: number;
      tabSize: number;
    },
    callbacks: EditorCallbacks,
  ) {
    this.kind = options.kind;
    this.dark = options.dark;
    this.callbacks = callbacks;
    document.documentElement.style.setProperty("--editor-font-size", `${options.fontSize}px`);

    this.view = new EditorView({
      parent,
      state: EditorState.create({
        doc: options.text,
        extensions: this.buildExtensions(options),
      }),
    });

    this.attachImageHandlers();
    this.reportSelection();
  }

  private buildExtensions(options: {
    kind: DocKind;
    dark: boolean;
    wordWrap: boolean;
    lineNumbers: boolean;
    fontSize: number;
    tabSize: number;
  }): Extension[] {
    return [
      this.gutterConf.of(options.lineNumbers ? [lineNumbers(), highlightActiveLineGutter(), foldGutter()] : []),
      highlightSpecialChars(),
      history(),
      drawSelection(),
      dropCursor(),
      EditorState.allowMultipleSelections.of(true),
      indentOnInput(),
      indentUnit.of(" ".repeat(Math.max(2, options.tabSize))),
      bracketMatching(),
      rectangularSelection(),
      highlightActiveLine(),
      highlightSelectionMatches(),
      this.wrapConf.of(options.wordWrap ? EditorView.lineWrapping : []),
      this.languageConf.of(languageExtension(options.kind)),
      this.themeConf.of(editorTheme(options.dark)),
      EditorView.updateListener.of((update) => {
        if (update.docChanged) {
          this.callbacks.onChange(update.state.doc.toString());
        }
        if (update.selectionSet || update.docChanged) {
          this.reportSelection();
        }
      }),
      keymap.of([
        { key: "Mod-s", run: () => { this.callbacks.onSave(); return true; } },
        indentWithTab,
        ...defaultKeymap,
        ...historyKeymap,
        ...searchKeymap,
        ...foldKeymap,
      ]),
      lintGutter(),
      linter((view) => this.lint(view), { delay: 600 }),
      placeholder("在这里开始写作，或把 Markdown 文件拖进窗口…"),
    ];
  }

  private async lint(view: EditorView): Promise<Diagnostic[]> {
    if (this.kind !== "markdown") {
      return [];
    }

    const text = view.state.doc.toString();
    const issues = await requestLint(text);
    if (text !== view.state.doc.toString()) {
      return [];
    }

    const doc = view.state.doc;
    return issues.map((issue) => {
      const lineNumber = Math.min(Math.max(1, issue.line), doc.lines);
      const line = doc.line(lineNumber);
      const column = Math.min(Math.max(1, issue.column), Math.max(1, line.length + 1));
      const from = Math.min(line.from + column - 1, line.to);
      const to = Math.min(from + Math.max(issue.length, 1), Math.max(line.to, from));
      return {
        from,
        to: Math.max(from, to),
        severity: "warning" as const,
        source: issue.rule,
        message: issue.message,
      };
    });
  }

  private attachImageHandlers(): void {
    const handleFiles = (files: FileList | null, event: Event) => {
      if (!files || files.length === 0) {
        return;
      }

      const images = Array.from(files).filter((file) => file.type.startsWith("image/"));
      if (images.length === 0) {
        return;
      }

      event.preventDefault();
      for (const image of images) {
        const reader = new FileReader();
        reader.onload = () => {
          const result = String(reader.result ?? "");
          const base64 = result.includes(",") ? result.slice(result.indexOf(",") + 1) : result;
          this.callbacks.onImagePaste(image.name || "image.png", base64, (relativePath) => {
            const alt = image.name.replace(/\.[^.]+$/, "");
            this.insertAtCursor(`![${alt}](${relativePath})`);
          });
        };
        reader.readAsDataURL(image);
      }
    };

    this.view.contentDOM.addEventListener("paste", (event) => {
      handleFiles((event as ClipboardEvent).clipboardData?.files ?? null, event);
    });

    this.view.contentDOM.addEventListener("drop", (event) => {
      handleFiles((event as DragEvent).dataTransfer?.files ?? null, event);
    });
  }

  insertAtCursor(text: string): void {
    const view = this.view;
    view.dispatch(view.state.replaceSelection(text));
    view.focus();
  }

  private reportSelection(): void {
    const { from, to, head } = this.view.state.selection.main;
    const line = this.view.state.doc.lineAt(head);
    this.callbacks.onSelection(line.number, head - line.from + 1, Math.abs(to - from));
  }

  getText(): string {
    return this.view.state.doc.toString();
  }

  setText(text: string, preserveCursor = false): void {
    if (text === this.getText()) {
      return;
    }

    if (preserveCursor) {
      const cursor = Math.min(this.view.state.selection.main.head, text.length);
      this.view.dispatch({ changes: { from: 0, to: this.view.state.doc.length, insert: text } });
      const position = Math.min(cursor, this.view.state.doc.length);
      this.view.dispatch({ selection: { anchor: position } });
    } else {
      this.view.dispatch({ changes: { from: 0, to: this.view.state.doc.length, insert: text } });
    }
  }

  setKind(kind: DocKind): void {
    if (this.kind === kind) {
      return;
    }

    this.kind = kind;
    this.view.dispatch({
      effects: this.languageConf.reconfigure(languageExtension(kind)),
    });
    forceLinting(this.view);
  }

  setTheme(dark: boolean): void {
    this.dark = dark;
    this.view.dispatch({ effects: this.themeConf.reconfigure(editorTheme(dark)) });
  }

  setWordWrap(enabled: boolean): void {
    this.view.dispatch({
      effects: this.wrapConf.reconfigure(enabled ? EditorView.lineWrapping : []),
    });
  }

  setLineNumbers(enabled: boolean): void {
    this.view.dispatch({
      effects: this.gutterConf.reconfigure(enabled ? [lineNumbers(), highlightActiveLineGutter(), foldGutter()] : []),
    });
  }

  setFontSize(size: number): void {
    document.documentElement.style.setProperty("--editor-font-size", `${size}px`);
  }

  focus(): void {
    this.view.focus();
  }

  scrollElement(): HTMLElement | null {
    return this.view.scrollDOM;
  }
}
