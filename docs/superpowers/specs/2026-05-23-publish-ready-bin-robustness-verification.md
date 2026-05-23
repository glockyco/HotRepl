# Bin robustness verification results

Both bins (`hotrepl`, `hotrepl-mcp`) were verified against tarballs built from the implementation
commits and installed into `/tmp/hotrepl-smoke` via `npm install --no-fund`.

## Environment

- Node: v25.9.0 (used for tarball smoke tests)
- Bun: 1.3.14 (used for unit tests + harness)
- Platform: macOS arm64
- Live backend: Ardenfall Demo on Steam via CrossOver, HotRepl.BepInEx plugin with the
  `ardenfall-compendium` mod, listening on `ws://127.0.0.1:18590`.

## Unit test suite

```
bun test packages/sdk packages/testing packages/mcp packages/cli
46 pass, 0 fail across 9 files
```

(45 + the new SessionManager concurrent-getSession dedupe test added during verification.)

## Live verification harness

`scripts/verify-live-ardenfall.ts` exercises both bins against a running Ardenfall instance.
**Local-only**: CI cannot launch a Unity game, so this script is invoked manually by a maintainer
after `bun run hotrepl:launch` in the `ardenfall-compendium` checkout.

Final run output:

```
Backend reachable.

CLI checks:
  ✓ hotrepl info --json returns a handshake — exit=0, host.name=BepInEx
  ✓ hotrepl eval Application.productName mentions Ardenfall —
    exit=0, stdout=""Ardenfall Demo 2025""
  ✓ hotrepl run compendium.info returns success — exit=0, stdout_bytes=99

MCP stdio checks:
  ✓ MCP tools/list returns 9 tools — count=9
  ✓ MCP hotrepl_eval Application.productName mentions Ardenfall
  ✓ MCP hotrepl_run compendium.info returns non-empty result — bytes=124
  ✓ MCP background refresh emitted notifications/tools/list_changed — seen
  ✓ MCP stderr empty (no stack traces on shutdown) — empty
  ✓ MCP exited cleanly (exit 0) — code=0

9/9 checks passed.
```

## Unreachable-backend smoke (CI-eligible)

These two smoke tests do NOT need a running game and can run on any host:

### MCP — unreachable backend

```
HOTREPL_URL=ws://127.0.0.1:1 ( ... initialize + tools/list + tools/call ... ) \
  | node node_modules/.bin/hotrepl-mcp
```

- stdout: 3 JSON-RPC responses (initialize, tools/list with 9 tools, tools/call result with
  `isError: true` and message
  `"HotRepl is not reachable at the configured URL. Make sure your Unity game
  with the HotRepl plugin (BepInEx) or mod (MelonLoader) is running."`)
- stderr: empty (no stack trace)
- exit: 0 (stdin EOF closed transport cleanly)

### CLI — unreachable backend

```
HOTREPL_URL=ws://127.0.0.1:1 node node_modules/.bin/hotrepl info --json
```

- stderr: `HotRepl WebSocket connection failed.` (one line, no stack trace)
- stdout: empty
- exit: 69 (EX_UNAVAILABLE)

## Bugs found during verification (and fixed)

Three additional production bugs were caught by the live harness that the original 8-task plan
missed:

1. **`runCli` leaked its session** — `connect()` held the WebSocket open after the command finished;
   Node refused to drain the event loop. The CLI printed correct output and then hung forever. Fixed
   by wrapping `dispatchCommand` in `try/finally` that calls `session.close()`.

2. **`SessionManager` had no `close()`** — the MCP bin's `SIGTERM` handler closed the McpServer
   transport but left the backend WebSocket open, so the process never exited. Fixed by adding
   `SessionManager.close()` and chaining it through `createHotReplMcpServer`'s returned `close`
   callback and `runStdioMcpServer`'s shutdown handle.

3. **`SessionManager.getSession()` had a connect race** — `refreshAnnotations` (fire-and-forget) and
   the first `tools/call` handler both reached the `this.session === undefined` check
   simultaneously. Each called `connect()`, opening two WebSockets; BepInEx's single-client policy
   evicted the first, surfacing as `session_evicted: displaced` on every subsequent tool call. Fixed
   by caching the in-flight connect promise.

4. **MCP bin ignored stdin EOF** — the MCP spec (Lifecycle section) makes stdin EOF the *primary*
   shutdown signal, but `StdioServerTransport` listens only for `data` and `error` on stdin. The bin
   now wires `process.stdin.once("end", ...)` alongside SIGINT/SIGTERM and guards every shutdown
   path with a 2-second watchdog (matching the Python SDK's fix in
   modelcontextprotocol/python-sdk#555) so the host MCP client never freezes waiting on us to exit.

All four are covered by the live harness above and by the unit tests in `packages/mcp/test/` and
`packages/cli/test/`.
