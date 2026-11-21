export interface LintIssue {
  line: number;
  column: number;
  length: number;
  rule: string;
  message: string;
  severity: "error" | "warning" | "info";
}

interface WorkerReply {
  id: number;
  issues: LintIssue[];
  error?: string;
}

let worker: Worker | null = null;
let nextId = 1;
let config: unknown = { default: true };
const pending = new Map<number, (issues: LintIssue[]) => void>();
let lastError: string | null = null;

function ensureWorker(): Worker {
  if (worker) {
    return worker;
  }

  worker = new Worker("lintWorker.js", { type: "module" });
  worker.onmessage = (event: MessageEvent<WorkerReply>) => {
    const reply = event.data;
    const resolve = pending.get(reply.id);
    if (resolve) {
      pending.delete(reply.id);
      if (reply.error) {
        lastError = reply.error;
      }
      resolve(reply.issues ?? []);
    }
  };
  worker.onerror = (event) => {
    lastError = event.message || "lint worker 启动失败";
  };
  return worker;
}

export function setLintConfig(value: unknown): void {
  config = value ?? { default: true };
}

export function getLintError(): string | null {
  return lastError;
}

export function requestLint(text: string): Promise<LintIssue[]> {
  if (text.length > 400_000) {
    return Promise.resolve([]);
  }

  return new Promise<LintIssue[]>((resolve) => {
    const id = nextId++;
    const timeout = window.setTimeout(() => {
      pending.delete(id);
      resolve([]);
    }, 4000);

    pending.set(id, (issues) => {
      window.clearTimeout(timeout);
      resolve(issues);
    });

    ensureWorker().postMessage({ id, text, config });
  });
}
