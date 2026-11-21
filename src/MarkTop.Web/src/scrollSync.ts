import type { EditorView } from "@codemirror/view";

interface Anchor {
  source: number;
  preview: number;
}

/** Maps source scroll positions to preview positions using heading anchors, with a ratio fallback. */
export class ScrollSync {
  private anchors: Anchor[] = [];
  private locked = false;
  private rebuildTimer = 0;
  private frame = 0;

  constructor(
    private readonly getView: () => EditorView | null,
    private readonly preview: HTMLElement,
  ) {}

  attach(): void {
    const view = this.getView();
    view?.scrollDOM.addEventListener("scroll", this.onSourceScroll, { passive: true });
    this.preview.addEventListener("scroll", this.onPreviewScroll, { passive: true });
  }

  detach(): void {
    const view = this.getView();
    view?.scrollDOM.removeEventListener("scroll", this.onSourceScroll);
    this.preview.removeEventListener("scroll", this.onPreviewScroll);
    window.clearTimeout(this.rebuildTimer);
    cancelAnimationFrame(this.frame);
  }

  scheduleRebuild(text: string): void {
    window.clearTimeout(this.rebuildTimer);
    this.rebuildTimer = window.setTimeout(() => this.rebuild(text), 300);
  }

  private rebuild(text: string): void {
    const view = this.getView();
    if (!view) {
      this.anchors = [];
      return;
    }

    const offsets: number[] = [];
    const pattern = /^(#{1,6})[ \t]+\S/gm;
    let match: RegExpExecArray | null;
    while ((match = pattern.exec(text)) !== null) {
      offsets.push(match.index);
    }

    const headings = Array.from(
      this.preview.querySelectorAll<HTMLElement>("h1, h2, h3, h4, h5, h6"),
    );

    if (offsets.length === 0 || offsets.length !== headings.length) {
      this.anchors = [];
      return;
    }

    const docLength = Math.max(1, view.state.doc.length);
    this.anchors = offsets.map((offset, index) => {
      const safe = Math.min(offset, docLength - 1);
      let source = 0;
      try {
        source = view.lineBlockAt(safe).top;
      } catch {
        source = 0;
      }
      return { source, preview: headings[index].offsetTop };
    });
  }

  private mapWithAnchors(value: number, from: "source" | "preview"): number | null {
    if (this.anchors.length < 2) {
      return null;
    }

    const other = from === "source" ? "preview" : "source";
    const points = this.anchors.map((anchor) => ({ key: anchor[from], value: anchor[other] }));
    if (value <= points[0].key) {
      return points[0].value;
    }

    for (let index = 1; index < points.length; index++) {
      const previous = points[index - 1];
      const current = points[index];
      if (value <= current.key) {
        const span = current.key - previous.key;
        const progress = span <= 0 ? 0 : (value - previous.key) / span;
        return previous.value + progress * (current.value - previous.value);
      }
    }

    return points[points.length - 1].value;
  }

  private readonly onSourceScroll = (): void => {
    if (this.locked) {
      return;
    }

    cancelAnimationFrame(this.frame);
    this.frame = requestAnimationFrame(() => {
      const view = this.getView();
      if (!view) {
        return;
      }

      const sourceTop = view.scrollDOM.scrollTop;
      const maxSource = Math.max(1, view.scrollDOM.scrollHeight - view.scrollDOM.clientHeight);
      const maxPreview = Math.max(1, this.preview.scrollHeight - this.preview.clientHeight);
      const anchored = this.mapWithAnchors(sourceTop, "source");
      const target = anchored ?? (sourceTop / maxSource) * maxPreview;
      this.locked = true;
      this.preview.scrollTop = Math.max(0, Math.min(target, maxPreview));
      window.setTimeout(() => {
        this.locked = false;
      }, 60);
    });
  };

  private readonly onPreviewScroll = (): void => {
    if (this.locked) {
      return;
    }

    cancelAnimationFrame(this.frame);
    this.frame = requestAnimationFrame(() => {
      const view = this.getView();
      if (!view) {
        return;
      }

      const previewTop = this.preview.scrollTop;
      const maxSource = Math.max(1, view.scrollDOM.scrollHeight - view.scrollDOM.clientHeight);
      const maxPreview = Math.max(1, this.preview.scrollHeight - this.preview.clientHeight);
      const anchored = this.mapWithAnchors(previewTop, "preview");
      const target = anchored ?? (previewTop / maxPreview) * maxSource;
      this.locked = true;
      view.scrollDOM.scrollTop = Math.max(0, Math.min(target, maxSource));
      window.setTimeout(() => {
        this.locked = false;
      }, 60);
    });
  };
}
