import { sveltekit } from "@sveltejs/kit/vite";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

// Workspace packages advertise TypeScript source under "bun" and built ESM
// under "import". dist/ only exists after tsup runs in CI. Adding "bun" to
// Vite's resolver lets the site read workspace packages from source for both
// vite dev and vite build.
const workspaceConditions = ["bun"];

export default defineConfig({
  plugins: [tailwindcss(), sveltekit()],
  resolve: { conditions: workspaceConditions },
  ssr: { resolve: { conditions: workspaceConditions } },
});
