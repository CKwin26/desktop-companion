import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";

const currentDir = dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  resolve: {
    alias: {
      "@desktop-companion/shared-types": resolve(currentDir, "../../packages/shared-types/src/index.ts"),
      "@desktop-companion/companion-engine": resolve(currentDir, "../../packages/companion-engine/src/index.ts"),
      "@desktop-companion/ai-provider-none": resolve(currentDir, "../../packages/ai-provider-none/src/index.ts"),
    },
  },
  server: {
    port: 1420,
    strictPort: true,
  },
});
