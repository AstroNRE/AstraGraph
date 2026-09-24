import fs from "node:fs";
import path from "node:path";
import type { IncomingMessage, ServerResponse } from "node:http";
import type { Plugin } from "vite";

type SpriteFile = { rsi: string; state: string; file: string };

const texturesRoot = process.env.ASTRA_TEXTURES ?? "/workspaces/night-city/Resources/Textures";

export function buildTexturesPlugin(): Plugin {
  let cache: SpriteFile[] | null = null;

  function load(): SpriteFile[] {
    if (cache) return cache;
    cache = [];
    if (!fs.existsSync(texturesRoot)) return cache;
    const stack = [texturesRoot];
    while (stack.length > 0) {
      const dir = stack.pop();
      if (!dir) continue;
      let entries: fs.Dirent[];
      try {
        entries = fs.readdirSync(dir, { withFileTypes: true });
      } catch {
        continue;
      }
      for (const entry of entries) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
          stack.push(full);
          continue;
        }
        if (!entry.name.endsWith(".png")) continue;
        const rel = path.relative(texturesRoot, full).split(path.sep).join("/");
        const folder = path.posix.dirname(rel);
        cache.push({
          rsi: folder === "." ? "/Textures" : `/Textures/${folder}`,
          state: entry.name.slice(0, -4),
          file: rel
        });
      }
    }
    return cache;
  }

  function handle(req: IncomingMessage, res: ServerResponse, next: (err?: unknown) => void) {
    const raw = req.url ?? "";
    if (!raw.startsWith("/build-textures")) {
      next();
      return;
    }
    const url = new URL(raw, "http://127.0.0.1");
    if (url.pathname === "/build-textures/index.json") {
      const query = (url.searchParams.get("q") ?? "").toLowerCase();
      const items = load()
        .filter((item) => !query || `${item.rsi} ${item.state}`.toLowerCase().includes(query))
        .slice(0, 72)
        .map((item) => ({ ...item, url: `/build-textures/file/${item.file}` }));
      res.setHeader("content-type", "application/json");
      res.end(JSON.stringify(items));
      return;
    }
    if (url.pathname.startsWith("/build-textures/file/")) {
      const rel = decodeURIComponent(url.pathname.slice("/build-textures/file/".length));
      const file = path.resolve(texturesRoot, rel);
      const root = path.resolve(texturesRoot);
      if (!file.startsWith(root + path.sep) || !fs.existsSync(file)) {
        res.statusCode = 404;
        res.end();
        return;
      }
      res.setHeader("content-type", "image/png");
      fs.createReadStream(file).pipe(res);
      return;
    }
    next();
  }

  return {
    name: "astra-build-textures",
    configureServer(server) {
      server.middlewares.use(handle);
    },
    configurePreviewServer(server) {
      server.middlewares.use(handle);
    }
  };
}
