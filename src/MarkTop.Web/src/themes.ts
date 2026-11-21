import { EditorView } from "@codemirror/view";
import { HighlightStyle, syntaxHighlighting } from "@codemirror/language";
import { tags as t } from "@lezer/highlight";
import type { Extension } from "@codemirror/state";

const darkHighlight = HighlightStyle.define([
  { tag: t.heading, color: "#569cd6", fontWeight: "600" },
  { tag: [t.strong], color: "#d7ba7d", fontWeight: "700" },
  { tag: t.emphasis, color: "#d7ba7d", fontStyle: "italic" },
  { tag: t.strikethrough, color: "#9a9a9a", textDecoration: "line-through" },
  { tag: [t.link, t.url], color: "#4ec9b0" },
  { tag: [t.monospace], color: "#ce9178" },
  { tag: t.quote, color: "#6a9955" },
  { tag: [t.keyword, t.modifier, t.operatorKeyword], color: "#c586c0" },
  { tag: [t.string, t.special(t.string)], color: "#ce9178" },
  { tag: [t.number, t.bool, t.null], color: "#b5cea8" },
  { tag: [t.propertyName, t.attributeName], color: "#9cdcfe" },
  { tag: [t.typeName, t.className, t.tagName], color: "#4ec9b0" },
  { tag: [t.comment, t.lineComment, t.blockComment], color: "#6a9955", fontStyle: "italic" },
  { tag: [t.punctuation, t.separator, t.bracket], color: "#9a9a9a" },
  { tag: t.invalid, color: "#f14c4c" },
]);

const lightHighlight = HighlightStyle.define([
  { tag: t.heading, color: "#0a3069", fontWeight: "600" },
  { tag: [t.strong], color: "#953800", fontWeight: "700" },
  { tag: t.emphasis, color: "#953800", fontStyle: "italic" },
  { tag: t.strikethrough, color: "#6e7781", textDecoration: "line-through" },
  { tag: [t.link, t.url], color: "#0a7d6b" },
  { tag: [t.monospace], color: "#a31515" },
  { tag: t.quote, color: "#116329" },
  { tag: [t.keyword, t.modifier, t.operatorKeyword], color: "#af00db" },
  { tag: [t.string, t.special(t.string)], color: "#a31515" },
  { tag: [t.number, t.bool, t.null], color: "#098658" },
  { tag: [t.propertyName, t.attributeName], color: "#0451a5" },
  { tag: [t.typeName, t.className, t.tagName], color: "#267f99" },
  { tag: [t.comment, t.lineComment, t.blockComment], color: "#008000", fontStyle: "italic" },
  { tag: [t.punctuation, t.separator, t.bracket], color: "#57606a" },
  { tag: t.invalid, color: "#c72e2e" },
]);

function themeBase(dark: boolean): Extension {
  return EditorView.theme(
    {
      "&": {
        color: "var(--fg)",
        backgroundColor: "transparent",
        height: "100%",
        fontSize: "var(--editor-font-size, 14px)",
      },
      ".cm-content": {
        caretColor: "var(--fg)",
        fontFamily: "var(--font-mono)",
      },
      "&.cm-focused": { outline: "none" },
      ".cm-scroller": { fontFamily: "var(--font-mono)" },
      ".cm-gutters": { backgroundColor: "transparent", color: "var(--fg-faint)", border: "none" },
      ".cm-lineNumbers .cm-gutterElement": { padding: "0 8px 0 12px" },
      ".cm-activeLine": { backgroundColor: "color-mix(in srgb, var(--fg) 4%, transparent)" },
      ".cm-selectionBackground, ::selection": { backgroundColor: "var(--selection) !important" },
      ".cm-cursor, .cm-dropCursor": { borderLeftColor: "var(--fg)" },
      ".cm-foldPlaceholder": {
        backgroundColor: "var(--bg-input)",
        border: "1px solid var(--border)",
        color: "var(--fg-muted)",
      },
      ".cm-tooltip": {
        backgroundColor: "var(--bg-elevated)",
        border: "1px solid var(--border)",
        color: "var(--fg)",
      },
      ".cm-diagnostic": { fontFamily: "var(--font-ui)", fontSize: "12px" },
    },
    { dark },
  );
}

export function editorTheme(dark: boolean): Extension {
  return [themeBase(dark), syntaxHighlighting(dark ? darkHighlight : lightHighlight)];
}
