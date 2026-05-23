import { defineConfig } from "tsup";

// Two entry points:
//   src/index.ts  — library, consumed by external code or other packages
//   src/bin.ts    — stdio executable, published as bin/hotrepl-mcp

const external = ["@hotrepl/sdk", "@modelcontextprotocol/sdk", "zod"];

export default defineConfig([
  {
    clean: true,
    dts: true,
    entry: ["src/index.ts"],
    external,
    format: ["esm"],
  },
  {
    // banner injects the shebang; source keeps no shebang so tsup doesn't
    // produce two conflicting lines.
    banner: { js: "#!/usr/bin/env node" },
    dts: false,
    entry: { bin: "src/bin.ts" },
    external,
    format: ["esm"],
  },
]);
