# @hotrepl/mcp

## 3.0.1

### Patch Changes

- Updated dependencies
  [[`74d2b68`](https://github.com/glockyco/HotRepl/commit/74d2b68069ceaa502f8349aee439b2b4548e90b0),
  [`5725a51`](https://github.com/glockyco/HotRepl/commit/5725a51cef3db2b46475c6d007b94b1de6b742e7)]:
  - @hotrepl/sdk@4.0.0

## 3.0.0

### Major Changes

- [`f6a4dcf`](https://github.com/glockyco/HotRepl/commit/f6a4dcf0e3a752914845f99b5aa00edbd7712d4b)
  Thanks [@glockyco](https://github.com/glockyco)! - Make the HotRepl MCP stdio server safe to run
  as a long-lived agent backend.

  - The `hotrepl-mcp` bin now installs proper signal handlers and exits cleanly on `SIGINT`,
    `SIGTERM`, and stdin EOF; an internal watchdog enforces the graceful-shutdown deadline.
  - `createHotReplMcpServer` returns `CreateHotReplMcpServerResult`, which exposes the new shutdown
    handle and a `listCommandDescriptors` helper. Consumers that embed the MCP server should hold
    the result and call its shutdown method when the host process tears down.
  - Tools are registered synchronously with conservative defaults at startup, so MCP clients see the
    full tool catalog immediately rather than after the backend connects.
  - `hotrepl_run` tool annotations now refresh in the background once the backend connects, so
    agents see accurate destructive/idempotency hints without paying a connection cost up front.
  - Concurrent `getSession` calls are deduped so the backend does not evict its own session.
  - Backend failures surface as `isError: true` tool results (per MCP spec) instead of throwing RPC
    errors.
  - The `hotrepl-mcp` bin now resolves to the compiled `dist/bin.js` instead of `src/index.ts`,
    matching the `dist/` build emitted by `tsup`.

  Migration:

  ```ts
  import { createHotReplMcpServer } from "@hotrepl/mcp";

  const { server, shutdown } = createHotReplMcpServer(/* options */);
  process.on("SIGTERM", () => shutdown());
  process.on("SIGINT", () => shutdown());
  ```

### Patch Changes

- Updated dependencies
  [[`f6a4dcf`](https://github.com/glockyco/HotRepl/commit/f6a4dcf0e3a752914845f99b5aa00edbd7712d4b)]:
  - @hotrepl/sdk@3.0.0
