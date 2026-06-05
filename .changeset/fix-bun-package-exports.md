---
"@hotrepl/protocol": patch
"@hotrepl/sdk": patch
"@hotrepl/cli": patch
"@hotrepl/mcp": patch
---

Fix Bun package exports in published npm packages.

The 4.0.0/3.0.1 packages declared a Bun-specific export target of `./src/index.ts`, but npm packages
only published `dist/`. Bun prefers the `bun` export condition, so Bun consumers could not import
`@hotrepl/sdk` or the other public packages from npm. Published Bun entrypoints now target the built
`dist/index.js` files, and the test gate builds packages before tests so workspace package imports
exercise the same public entrypoint shape.
