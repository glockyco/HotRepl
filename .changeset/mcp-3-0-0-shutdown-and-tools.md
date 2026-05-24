---
"@hotrepl/mcp": major
---

Make the HotRepl MCP stdio server safe to run as a long-lived agent backend.

- The `hotrepl-mcp` bin now installs proper signal handlers and exits cleanly on `SIGINT`,
  `SIGTERM`, and stdin EOF; an internal watchdog enforces the graceful-shutdown deadline.
- `createHotReplMcpServer` returns `CreateHotReplMcpServerResult`, which exposes the new shutdown
  handle and a `listCommandDescriptors` helper. Consumers that embed the MCP server should hold the
  result and call its shutdown method when the host process tears down.
- Tools are registered synchronously with conservative defaults at startup, so MCP clients see the
  full tool catalog immediately rather than after the backend connects.
- `hotrepl_run` tool annotations now refresh in the background once the backend connects, so agents
  see accurate destructive/idempotency hints without paying a connection cost up front.
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
