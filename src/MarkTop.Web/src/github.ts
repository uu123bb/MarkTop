/**
 * GitHub 风格扩展语法的增强：Lute 已经把 `> [!NOTE]` 解析成 `.callout[data-subtype]`，
 * 这里只做两件事——补上便于取色的类名、把标题本地化。
 */
const TITLES: Record<string, string> = {
  NOTE: "注意",
  TIP: "提示",
  IMPORTANT: "重要",
  WARNING: "警告",
  CAUTION: "危险",
};

export function enhanceGithubSyntax(root: HTMLElement): void {
  const callouts = Array.from(root.querySelectorAll<HTMLElement>(".callout"));
  for (const callout of callouts) {
    const subtype = (callout.dataset.subtype ?? "").toUpperCase();
    if (!subtype) {
      continue;
    }

    callout.classList.add("marktop-alert", `marktop-alert-${subtype.toLowerCase()}`);

    const localized = TITLES[subtype];
    const title = callout.querySelector<HTMLElement>(".callout-title");
    if (title && localized) {
      title.textContent = localized;
    }
  }
}
