import { defineConfig } from "tsup";

// Two entry points:
//   src/index.ts  — library API (runCli, CliRunResult, …)
//   src/bin.ts    — Node-compatible stdio executable published as bin/hotrepl

const external = ["@hotrepl/sdk"];

export default defineConfig([
  {
    clean: true,
    dts: true,
    entry: ["src/index.ts"],
    external,
    format: ["esm"],
  },
  {
    banner: { js: "#!/usr/bin/env node" },
    dts: false,
    entry: { bin: "src/bin.ts" },
    external,
    format: ["esm"],
  },
]);
