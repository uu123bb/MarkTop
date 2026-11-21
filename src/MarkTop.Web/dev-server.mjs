// Minimal static server for smoke-testing the web layer in a normal browser:
//   node dev-server.mjs   ->   http://localhost:4173
import { createServer } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { extname, join, normalize, resolve } from "node:path";

const root = resolve(process.cwd(), "dist");
const port = Number(process.env.PORT ?? 4173);

const types = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".mjs": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".svg": "image/svg+xml",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".woff": "font/woff",
  ".woff2": "font/woff2",
  ".ttf": "font/ttf",
  ".wasm": "application/wasm",
  ".map": "application/json",
};

createServer(async (request, response) => {
  const url = new URL(request.url ?? "/", `http://localhost:${port}`);
  const requested = url.pathname === "/" ? "/index.html" : url.pathname;
  const filePath = join(root, normalize(requested).replace(/^([/\\])+/, ""));

  if (!filePath.startsWith(root)) {
    response.writeHead(403);
    response.end("forbidden");
    return;
  }

  try {
    const info = await stat(filePath);
    if (!info.isFile()) {
      throw new Error("not a file");
    }

    const body = await readFile(filePath);
    response.writeHead(200, {
      "content-type": types[extname(filePath).toLowerCase()] ?? "application/octet-stream",
      "cache-control": "no-store",
    });
    response.end(body);
  } catch {
    response.writeHead(404, { "content-type": "text/plain; charset=utf-8" });
    response.end("not found");
  }
}).listen(port, "127.0.0.1", () => {
  console.log(`[dev] http://localhost:${port}`);
});
