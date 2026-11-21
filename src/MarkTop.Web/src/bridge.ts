export type HostMessage = { type: string; [key: string]: unknown };

type Handler = (message: HostMessage) => void;

const handlers = new Map<string, Handler>();
let fallbackQueue: HostMessage[] = [];
let ready = false;

export function on(type: string, handler: Handler): void {
  handlers.set(type, handler);
}

export function post(message: HostMessage): void {
  const webview = (window as unknown as { chrome?: { webview?: { postMessage: (m: unknown) => void } } }).chrome?.webview;
  if (webview) {
    webview.postMessage(message);
  } else {
    // Browser fallback: useful while developing the web layer without WPF.
    console.info("[bridge→host]", message);
  }
}

export function connect(): void {
  const webview = (window as unknown as {
    chrome?: { webview?: { addEventListener: (t: string, h: (e: MessageEvent) => void) => void } };
  }).chrome?.webview;

  if (!webview) {
    ready = true;
    fallbackQueue = [];
    post({ type: "ready", browser: true });
    return;
  }

  webview.addEventListener("message", (event: MessageEvent) => {
    const message = event.data as HostMessage;
    if (!message || typeof message.type !== "string") {
      return;
    }

    if (message.type === "init") {
      ready = true;
      for (const queued of fallbackQueue) {
        handlers.get(queued.type)?.(queued);
      }
      fallbackQueue = [];
    }

    const handler = handlers.get(message.type);
    if (handler) {
      handler(message);
    } else if (!ready) {
      fallbackQueue.push(message);
    }
  });

  post({ type: "ready" });
}

export function isHostAvailable(): boolean {
  return Boolean((window as unknown as { chrome?: { webview?: unknown } }).chrome?.webview);
}
