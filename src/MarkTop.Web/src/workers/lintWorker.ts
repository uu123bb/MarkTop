import { lint } from "markdownlint/sync";

interface Request {
  id: number;
  text: string;
  config?: unknown;
}

const ctx = self as unknown as {
  onmessage: ((event: MessageEvent<Request>) => void) | null;
  postMessage: (message: unknown) => void;
};

ctx.onmessage = (event: MessageEvent<Request>) => {
  const { id, text, config } = event.data;
  try {
    const results = lint({
      strings: { content: text },
      config: (config as Record<string, unknown>) ?? { default: true },
    });

    const issues = (results.content ?? []).map((item) => ({
      line: item.lineNumber,
      column: item.errorRange?.[0] ?? 1,
      length: item.errorRange?.[1] ?? 0,
      rule: item.ruleNames?.join("/") ?? "",
      message: item.errorDetail ? `${item.ruleDescription}：${item.errorDetail}` : item.ruleDescription,
      severity: "warning" as const,
    }));

    ctx.postMessage({ id, issues });
  } catch (error) {
    ctx.postMessage({ id, issues: [], error: String(error) });
  }
};
