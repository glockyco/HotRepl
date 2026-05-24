---
"@hotrepl/sdk": major
---

Add an explicit lifecycle and a catalog cache to `Session`.

- `RuntimeTransport` now requires a `close(): void` method so the SDK can release the underlying
  WebSocket when a long-running process exits. Any custom `RuntimeTransport` implementation must add
  a `close()` member.
- `Session.close()` is now public. Long-running consumers (servers, daemons, MCP) should call it on
  shutdown so the Node/Bun event loop can drain.
- `Session.run(name, args)` now uses a per-session catalog cache (`Session.listCommands()` and
  `Session.getCatalogEntry(name)`) and no longer issues a `command_describe` per call. The catalog
  is fetched on first use and reused for the lifetime of the session.
- Suppress the `undici` `uncaughtException` that fired when the underlying WebSocket failed to open;
  the resulting `HotReplConnectionError` is the only surfaced failure.

Migration:

```ts
// custom transports must now implement close()
class MyTransport implements RuntimeTransport {
  // ...existing members
  close(): void {
    /* release your socket here */
  }
}

// long-running consumers should explicitly close
const session = await connect();
try {
  /* … */
} finally {
  session.close();
}
```
