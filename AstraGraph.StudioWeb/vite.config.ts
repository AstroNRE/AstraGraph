import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5174,
    proxy: {
      "/api": "http://127.0.0.1:5173",
      "/health": "http://127.0.0.1:5173",
      "/ws": { target: "ws://127.0.0.1:5173", ws: true }
    }
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    rollupOptions: {
      output: {
        entryFileNames: "studio.js",
        chunkFileNames: "assets/[name].js",
        assetFileNames: (info) => {
          const name = info.names?.[0] ?? info.name ?? "";
          return name.endsWith(".css") ? "studio.css" : "assets/[name][extname]";
        }
      }
    }
  },
  test: {
    environment: "node"
  }
});
