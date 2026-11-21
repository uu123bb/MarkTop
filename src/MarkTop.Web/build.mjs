import { build, context } from "esbuild";
import { cpSync, existsSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));
const dist = join(root, "dist");
const watch = process.argv.includes("--watch");

const vditorRoot = join(root, "node_modules", "vditor", "dist");

/** Only the pieces we actually use: no mermaid extras, no 200 syntax themes, no mathjax. */
const vditorFiles = [
  "index.min.js",
  "index.css",
  "images/img-loading.svg",
  "js/lute/lute.min.js",
  "js/icons/ant.js",
  "js/i18n/zh_CN.js",
  "js/i18n/en_US.js",
  "js/highlight.js/highlight.min.js",
  "js/highlight.js/styles/atom-one-dark.min.css",
  "js/highlight.js/styles/github.min.css",
  "css/content-theme/light.css",
  "css/content-theme/dark.css",
  "css/content-theme/ant-design.css",
  "js/katex/katex.min.css",
  "js/katex/katex.min.js",
  "js/mermaid/mermaid.min.js",
];

const vditorDirectories = ["js/katex/fonts"];

function copyVendor() {
  const target = join(dist, "vendor", "vditor", "dist");
  const missing = [];
  for (const relative of vditorFiles) {
    const source = join(vditorRoot, relative);
    if (!existsSync(source)) {
      missing.push(relative);
      continue;
    }
    const destination = join(target, relative);
    mkdirSync(dirname(destination), { recursive: true });
    cpSync(source, destination);
  }

  for (const relative of vditorDirectories) {
    const source = join(vditorRoot, relative);
    if (!existsSync(source)) {
      missing.push(`${relative}/`);
      continue;
    }
    cpSync(source, join(target, relative), { recursive: true });
  }

  if (missing.length > 0) {
    console.warn(`[web] vditor assets missing: ${missing.join(", ")}`);
  }
}

function copyStatic() {
  cpSync(join(root, "src", "index.html"), join(dist, "index.html"));
  for (const file of ["tokens.css", "app.css", "print.css"]) {
    cpSync(join(root, "src", "assets", file), join(dist, file));
  }
}

const shared = {
  bundle: true,
  format: "esm",
  target: ["chrome120"],
  platform: "browser",
  logLevel: "info",
  legalComments: "none",
  minify: false,
  sourcemap: false,
};

async function runOnce() {
  // Incremental: never wipe the whole dist tree (bulk deletes are blocked by the sandbox and slow on
  // Windows); just refresh the generated entry files and let copies overwrite in place.
  mkdirSync(dist, { recursive: true });
  for (const file of ["app.js", "lintWorker.js", "index.html"]) {
    const target = join(dist, file);
    if (existsSync(target)) {
      rmSync(target, { force: true });
    }
  }

  await build({
    ...shared,
    entryPoints: [join(root, "src", "app.ts")],
    outfile: join(dist, "app.js"),
  });

  await build({
    ...shared,
    entryPoints: [join(root, "src", "workers", "lintWorker.ts")],
    outfile: join(dist, "lintWorker.js"),
  });

  copyStatic();
  copyVendor();
  writeFileSync(join(dist, "build-info.json"), JSON.stringify({ builtAt: new Date().toISOString() }, null, 2));
  console.log("[web] dist ready");
}

if (watch) {
  await runOnce();
  const rebuild = await context({
    ...shared,
    entryPoints: [join(root, "src", "app.ts")],
    outfile: join(dist, "app.js"),
  });
  await rebuild.watch();
  console.log("[web] watching…");
} else {
  await runOnce();
}
