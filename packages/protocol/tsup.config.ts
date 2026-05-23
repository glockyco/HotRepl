import { defineConfig } from "tsup";

export default defineConfig({
  clean: true,
  dts: true,
  entry: ["src/index.ts"],
  // typebox ships as a regular dep; keep it external so npm installs it.
  external: ["typebox"],
  format: ["esm"],
});
