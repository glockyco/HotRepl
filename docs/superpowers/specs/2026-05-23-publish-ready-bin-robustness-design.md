# Publish-ready bin robustness

**Date:** 2026-05-23\
**Status:** Draft — pending user review\
**Authors:** Opus

## Goal

A first-time user runs `npx -y @hotrepl/mcp` (or `npx -y @hotrepl/cli ...`) before their Unity game
is running. Today the MCP bin emits an uncaught stack trace and exits 0, which most MCP clients
(Claude Desktop, Cursor, Zed) surface as "MCP server crashed." The user sees no actionable error and
no path forward.

This spec defines the changes needed across `@hotrepl/sdk`, `@hotrepl/mcp`, and `@hotrepl/cli` so
that:

1. `npx -y @hotrepl/mcp` starts cleanly with no backend running, registers all nine tools, and
   surfaces "HotRepl is not reachable, please start your Unity game" as a tool-call `isError`
   envelope on the first invocation.
2. `npx -y @hotrepl/cli ...` exits with a sysexits-conformant code, prints a clean stderr message,
   and never shows a stack trace for an expected error.
3. Both bins handle SIGINT/SIGTERM gracefully — close the WebSocket transport, flush stdio, exit 130
   / 0.

## Background

### Observed failure (smoke-test, bootstrap publish dry-run)

Tarball install + run under real `node v25.9.0` with no backend on the test port:

```
$ HOTREPL_URL=ws://127.0.0.1:1 node node_modules/.bin/hotrepl-mcp
file:///.../node_modules/@hotrepl/sdk/dist/index.js:439
        failOpen(new Error("HotRepl WebSocket connection failed."));
                 ^

Error: HotRepl WebSocket connection failed.
    at WebSocket.<anonymous> (.../dist/index.js:439:18)
    at [nodejs.internal.kHybridDispatch] (node:internal/event_target:843:20)
    at WebSocket.dispatchEvent (node:internal/event_target:776:26)
    at fireEvent (node:internal/deps/undici/undici:14147:14)
    at #onSocketClose (node:internal/deps/undici/undici:15449:11)
exit=0
```

CLI surfaces a clean message but exits 0 too:

```
$ HOTREPL_URL=ws://127.0.0.1:1 node node_modules/.bin/hotrepl info --json
HotRepl WebSocket connection failed.
exit=0
```

### Root causes

1. **MCP eager connect.** `createHotReplTools(manager)` (in `packages/mcp/src/tools.ts`) awaits
   `manager.getSession()` and `listCommandDescriptors(session)` at server startup, purely to compute
   `runMutates` for the `hotrepl_run` tool's `destructiveHint` annotation. When the backend is
   unreachable, that await rejects through `await
   createHotReplMcpServer()` and out of
   `runStdioMcpServer()` to the bin's top-level await, which has no error handler.

2. **SDK uncaught-error trace.** `WebSocketTransport.open()` registers an error handler via the W3C
   `addEventListener('error', ...)`. The handler calls `failOpen(new Error(...))`, which rejects the
   open() promise. The rejection IS handled by the caller's await chain — but undici's internal
   EventEmitter also dispatches the same 'error' event, and Node's EventEmitter unconditionally
   throws when 'error' is emitted with no `.on('error', ...)` listener. The W3C-style
   `addEventListener` does not register a Node-style listener, so undici sees no listener and
   re-throws.

3. **CLI exit-code-0-on-error.** The CLI bin uses ESM top-level await:

   ```ts
   const result = await runCli(process.argv.slice(2), { env: process.env });
   if (result.stderr.length > 0) process.stderr.write(result.stderr);
   process.exit(result.exitCode);
   ```

   `runCli` catches every error and returns a `CliRunResult` with a non-zero `exitCode`, so the
   message-and-exit path works correctly. The exit=0 observed in the smoke test came from a
   **different scenario** that wasn't in the runCli try/catch (the SDK uncaught-error trace fires
   synchronously before `process.exit` runs). Fixing the SDK issue (#2) eliminates the exit-0
   surprise for the CLI.

4. **No signal handling.** Neither bin installs SIGINT or SIGTERM handlers. `Ctrl-C` exits 130 by
   Node's default, but the WebSocket transport is left half-open and any in-flight request is
   abandoned. The MCP spec recommends servers respond to SIGTERM by closing the transport gracefully
   (see References).

### Best-practice references (validated 2026-05-23)

| Concern                                | Reference                                                                                                                                                          |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| MCP tool-error envelope                | https://modelcontextprotocol.io/specification/2025-11-25/server/tools — `isError: true` for execution failures, JSON-RPC error for protocol failures               |
| MCP lazy-connect pattern               | https://github.com/modelcontextprotocol/servers-archived/blob/main/src/github/index.ts and `brave-search/index.ts` — neither contacts the backend at startup       |
| MCP conservative annotation defaults   | https://blog.modelcontextprotocol.io/posts/2026-03-16-tool-annotations/ — `destructiveHint: true, readOnlyHint: false, openWorldHint: true, idempotentHint: false` |
| MCP `notifications/tools/list_changed` | Verified in `@modelcontextprotocol/sdk@1.29.0` — `RegisteredTool.update()` calls `sendToolListChanged()`                                                           |
| MCP stdio shutdown                     | https://modelcontextprotocol.io/specification/2025-11-25/basic/transports — client closes stdin, sends SIGTERM, then SIGKILL                                       |
| Node CLI exit codes                    | https://man7.org/linux/man-pages/man3/sysexits.h.3head.html — 64 EX_USAGE, 69 EX_UNAVAILABLE, 70 EX_SOFTWARE, 75 EX_TEMPFAIL, 77 EX_NOPERM                         |
| Node SIGINT exit code                  | https://nodejs.org/api/process.html#signal-events — 128 + signal number = 130 for SIGINT                                                                           |
| process.exitCode vs process.exit       | https://nodejs.org/api/process.html#processexitcode — `process.exit()` truncates writes; prefer `exitCode + return`                                                |
| Top-level-await rejection              | https://nodejs.org/api/esm.html#top-level-await — unsettled top-level await exits 13; rejected main promise exits 1                                                |
| npm exit-handler reference             | https://raw.githubusercontent.com/npm/cli/latest/lib/cli/exit-handler.js — flush streams before forced exit                                                        |

## Architecture

Three layers of fixes, one per package, each independently testable:

### Layer 1 — `@hotrepl/sdk`

**Goal:** A failed `connect()` must reject the returned promise cleanly. No stderr stack trace.

**Change A — Fix the uncaught error.** The WebSocketTransport's open() path registers W3C-style
listeners (`addEventListener`). Undici's EventEmitter fires 'error' with no Node-style listener
attached and the event re-throws. The fix is to add a **Node-style no-op error listener** on the
same socket so EventEmitter sees that 'error' has a listener and stops re-throwing:

```ts
// In WebSocketTransport.open(), after construction:
this.socket.addEventListener("error", () => {
  failOpen(new Error("HotRepl WebSocket connection failed."));
});
// Belt-and-braces: prevent undici's internal EventEmitter from re-throwing
// when no Node-style listener exists.
(this.socket as unknown as NodeJS.EventEmitter).on?.("error", () => {});
```

The cast keeps the W3C type at the call site while allowing the Node-style hook in environments
(undici, ws) that emit through EventEmitter. In browsers, `.on` is undefined and the `?.`
short-circuits — no behavioral change.

**Change B — Add `Session.close()`.** Today `Session` exposes no shutdown method. Add:

```ts
class Session {
  close(): void {
    this.transport.close();
  }
}
```

The MCP and CLI bins call this from their SIGTERM handlers.

### Layer 2 — `@hotrepl/mcp`

**Goal:** Server starts without contacting the backend. Tool invocations return clean `isError`
envelopes when the backend is unreachable. When the backend becomes reachable, annotations refresh
automatically.

**Change A — `createHotReplTools` becomes synchronous.** Drop the eager `await manager.getSession()`
and `await listCommandDescriptors(session)`. Register `hotrepl_run` with **conservative MCP-spec
defaults**:

```ts
{ destructiveHint: true, readOnlyHint: false }
```

These match the spec's defaults
(`destructiveHint: true, readOnlyHint: false,
openWorldHint: true, idempotentHint: false`) — so even
a malicious or unreachable backend can't trick a client into treating `hotrepl_run` as read-only.

The signature changes from `async (manager): Promise<HotReplMcpTool[]>` to
`(manager): HotReplMcpTool[]`.

**Change B — Background annotation refresh.** After `server.connect(stdio)` returns in
`runStdioMcpServer`, kick off a fire-and-forget refresh:

```ts
async function refreshHotReplRunAnnotations(
  manager: SessionManager,
  registered: RegisteredTool,
): Promise<void> {
  try {
    const session = await manager.getSession();
    const commands = await listCommandDescriptors(session);
    const runMutates = commands.some((c) => c.mutatesState);
    registered.update({
      annotations: { destructiveHint: runMutates, readOnlyHint: !runMutates },
    });
  } catch {
    // Backend unreachable; keep conservative defaults. Refresh is best-effort.
  }
}
```

`RegisteredTool.update()` auto-sends `notifications/tools/list_changed`, so MCP clients that
subscribed to that notification (Zed, future Claude Desktop) pick up the precise annotations once
the backend is reachable.

`createHotReplMcpServer` now returns both the server and a function the bin calls after
`connect(stdio)`:

```ts
async function createHotReplMcpServer(options): Promise<{
  server: McpServer;
  refreshAnnotations(): Promise<void>;
}>;
```

**Change C — Tool-handler error envelopes.** Each handler currently throws on backend errors. Wrap
every handler so SDK errors become MCP `isError` results with an actionable message:

```ts
function safeTool(handler) {
  return async (args) => {
    try {
      return await handler(args);
    } catch (error) {
      return {
        content: [{
          type: "text",
          text: formatBackendError(error),
        }],
        isError: true,
      };
    }
  };
}

function formatBackendError(error: unknown): string {
  if (error instanceof HotReplError) return error.message;
  if (isConnectionFailure(error)) {
    return `HotRepl is not reachable at ${url}. Make sure your Unity game with the HotRepl plugin (BepInEx) or mod (MelonLoader) is running.`;
  }
  return error instanceof Error ? error.message : String(error);
}
```

**Change D — `packages/mcp/src/bin.ts` hardening.** Add top-level try/catch, signal handlers, and
stderr-only logging:

```ts
import { runStdioMcpServer } from "./index.js";

const EX_OK = 0;
const EX_SOFTWARE = 70;

async function main() {
  const shutdown = await runStdioMcpServer({ env: process.env });
  const onSignal = (signal: NodeJS.Signals) => {
    void shutdown();
    process.exit(signal === "SIGINT" ? 130 : EX_OK);
  };
  process.on("SIGINT", onSignal);
  process.on("SIGTERM", onSignal);
}

try {
  await main();
} catch (error) {
  // Stdio servers MUST NOT write to stdout outside the MCP protocol.
  process.stderr.write(
    `hotrepl-mcp: ${error instanceof Error ? error.message : String(error)}\n`,
  );
  process.exitCode = EX_SOFTWARE;
}
```

`runStdioMcpServer` returns a `shutdown` function so the bin can request the server to close stdin
and the WS transport cleanly.

### Layer 3 — `@hotrepl/cli`

**Goal:** Defense-in-depth around the bin. `runCli` already returns structured results; this layer
ensures the bin itself never crashes with a stack trace.

**Change A — `packages/cli/src/bin.ts` hardening.**

```ts
import { runCli } from "./index.js";

const EX_SOFTWARE = 70;

async function main() {
  const result = await runCli(process.argv.slice(2), { env: process.env });
  if (result.stdout.length > 0) process.stdout.write(result.stdout);
  if (result.stderr.length > 0) process.stderr.write(result.stderr);
  process.exitCode = result.exitCode;
}

process.on("SIGINT", () => {
  process.exit(130);
});

try {
  await main();
} catch (error) {
  process.stderr.write(
    `hotrepl: ${error instanceof Error ? error.message : String(error)}\n`,
  );
  process.exitCode = EX_SOFTWARE;
}
```

Note: `process.exitCode + return` (no `process.exit`) lets stdout/stderr drain naturally.
`process.exit` stays only in the SIGINT handler where we deliberately cut off.

## Data flow

```
$ npx -y @hotrepl/mcp                        (game not running)
       │
       ▼
bin.ts main() ─── try ──────────────────────────────────┐
       │                                                │
       ▼                                                │
runStdioMcpServer(opts)                                 │
       │                                                │
       ├─→ createHotReplMcpServer(opts)                 │
       │     ├─→ new SessionManager(opts)               │
       │     ├─→ new McpServer(...)                     │
       │     ├─→ createHotReplTools(manager)  ← SYNC    │
       │     │     │                                    │
       │     │     └─→ returns [9 tools, conservative]  │
       │     └─→ for tool → server.registerTool(...)    │
       │                                                │
       ├─→ server.connect(new StdioServerTransport())   │
       │                                                │
       └─→ void refreshAnnotations()  ← background      │
              │                                         │
              └─→ try { connect → list → update }       │
                  catch { swallow }                     │
                                                        │
       ▼                                                │
[server ready, accepting MCP requests on stdio]         │
       │                                                │
       ▼                                                │
agent calls tools/list → returns 9 tools with           │
                       conservative annotations         │
       │                                                │
       ▼                                                │
agent calls hotrepl_eval → handler awaits getSession()  │
                          → connect rejects             │
                          → catch → isError envelope    │
                          → "HotRepl not reachable…"    │
                                                        │
       ▼                                                │
SIGTERM (agent closes server) ──────────────────────────┤
       │                                                │
       └─→ shutdown() closes WS + stdin → exit 0        │
                                                        │
catch (error) ──────────────────────────────────────────┘
       └─→ stderr write + exitCode = 70
```

## Components

### Files changed

| File                                      | Change                                                                                                             |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `packages/sdk/src/websocket-transport.ts` | Add Node-style no-op `.on('error')` listener (Layer 1A)                                                            |
| `packages/sdk/src/session.ts`             | Add `Session.close()` (Layer 1B)                                                                                   |
| `packages/sdk/src/index.ts`               | Re-export nothing new — `close` is on Session instance                                                             |
| `packages/mcp/src/tools.ts`               | `createHotReplTools` becomes sync, conservative defaults, `safeTool` wrapper, `formatBackendError` (Layer 2A, 2C)  |
| `packages/mcp/src/index.ts`               | `createHotReplMcpServer` returns `{server, refreshAnnotations}`; `runStdioMcpServer` returns `shutdown` (Layer 2B) |
| `packages/mcp/src/bin.ts`                 | Top-level try/catch, SIGINT/SIGTERM handlers, stderr logging (Layer 2D)                                            |
| `packages/cli/src/bin.ts`                 | Defense-in-depth try/catch, SIGINT handler, `exitCode + return` pattern (Layer 3A)                                 |

### Files added

| File                                            | Purpose                                                                                            |
| ----------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| `packages/sdk/test/websocket-transport.test.ts` | Verify no uncaught error reaches stderr on connect failure                                         |
| `packages/mcp/test/lifecycle.test.ts`           | Verify startup without backend; verify tool-invocation isError envelope; verify annotation refresh |
| `packages/cli/test/bin-exit-codes.test.ts`      | Verify exit code per error class                                                                   |

### Public API additions

- `Session.close(): void` — closes the underlying WebSocket transport. **Idempotent**: must be safe
  to call N times. Implementation guards via a `closed` boolean or by tolerating the underlying
  transport's close throwing on a second call.
- `runStdioMcpServer(opts): Promise<() => Promise<void>>` — returns a `shutdown` function instead of
  resolving void. Callers update from `await runStdioMcpServer(opts)` to
  `const shutdown = await runStdioMcpServer(opts)`. **Idempotent shutdown**: calling the returned
  function multiple times closes once. The returned promise resolves after the WS transport is
  closed and any in-flight `refreshAnnotations` is cancelled or completed.
- `createHotReplMcpServer(opts): Promise<{server, refreshAnnotations}>` — signature change.
  Internal-only; not documented as user-facing API. `refreshAnnotations` is **idempotent**: calling
  it while a previous call is in-flight is a no-op (or returns the existing promise).
- `createHotReplTools(manager): HotReplMcpTool[]` — signature change from async to sync.

Versioning impact: these are technically breaking changes to exported symbols. Since neither package
has been published yet (the bootstrap publish is what motivated this spec), there is no real
breakage. The first published version (2.0.0) ships with these signatures.

## Error handling

| Failure                             | Where caught                   | User-visible result                                          | Exit code                |
| ----------------------------------- | ------------------------------ | ------------------------------------------------------------ | ------------------------ |
| MCP startup, invalid env config     | mcp bin try/catch              | stderr: `hotrepl-mcp: <reason>`                              | 64 (EX_USAGE)            |
| MCP startup, backend unreachable    | not caught — startup continues | —                                                            | —                        |
| MCP tool call, backend unreachable  | `safeTool` wrapper             | `CallToolResult.isError: true` with "HotRepl not reachable…" | — (server keeps running) |
| MCP tool call, evaluator error      | `safeTool` wrapper             | `CallToolResult.isError: true` with HotReplError message     | —                        |
| MCP internal bug                    | bin try/catch                  | stderr: `hotrepl-mcp: <reason>`                              | 70 (EX_SOFTWARE)         |
| MCP SIGINT                          | signal handler                 | shutdown + flush + exit                                      | 130                      |
| MCP SIGTERM                         | signal handler                 | shutdown + flush + exit                                      | 0 (graceful)             |
| CLI invocation, backend unreachable | `runCli` (existing)            | stderr: "HotRepl WebSocket connection failed."               | 69 (EX_UNAVAILABLE)      |
| CLI invocation, invalid args        | `runCli` (existing)            | stderr: usage message                                        | 64 (EX_USAGE)            |
| CLI internal bug                    | bin try/catch                  | stderr: `hotrepl: <reason>`                                  | 70 (EX_SOFTWARE)         |
| CLI SIGINT                          | signal handler                 | exit 130                                                     | 130                      |

## Testing

TDD per change. Each test fails before the corresponding implementation.

### SDK tests

```ts
// packages/sdk/test/websocket-transport.test.ts
test("connect to an unreachable port rejects without uncaught error", async () => {
  const uncaughtListener = mock(() => {});
  process.on("uncaughtException", uncaughtListener);
  process.on("unhandledRejection", uncaughtListener);
  try {
    await connect({ url: "ws://127.0.0.1:1" }); // privileged, immediate-fail
    expect.unreachable();
  } catch (error) {
    expect(error).toBeInstanceOf(Error);
    expect((error as Error).message).toContain("WebSocket connection failed");
  }
  // Give the event loop a tick to surface any deferred uncaught error
  await new Promise((r) => setImmediate(r));
  expect(uncaughtListener).not.toHaveBeenCalled();
});
```

### MCP tests

```ts
// packages/mcp/test/lifecycle.test.ts
test("createHotReplTools returns synchronously without contacting backend", () => {
  const unreachable: RuntimeTransport = {
    open() {
      throw new Error("backend not contacted in this test");
    },
    /* ... stubs ... */
  };
  const manager = new SessionManager({ runtime: unreachable });
  const tools = createHotReplTools(manager);
  expect(tools.map((t) => t.name)).toHaveLength(9);
  expect(tools.find((t) => t.name === "hotrepl_run")?.annotations)
    .toMatchObject({ destructiveHint: true, readOnlyHint: false });
});

test("tool invocation surfaces backend unreachable as isError", async () => {
  const failOnOpen: RuntimeTransport = {
    open() {
      throw new Error("HotRepl WebSocket connection failed.");
    },
    /* ... */
  };
  const manager = new SessionManager({ runtime: failOnOpen });
  const tools = createHotReplTools(manager);
  const eval_ = tools.find((t) => t.name === "hotrepl_eval");
  const result = await eval_!.handler({ code: "1+1" });
  expect(result.isError).toBe(true);
  expect(result.content[0].text).toContain("HotRepl is not reachable");
});

test("refreshAnnotations updates hotrepl_run annotations when backend reachable", async () => {
  // Drive the MCP protocol via InMemoryTransport so the test observes
  // exactly what a client would see, including the
  // notifications/tools/list_changed round-trip.
  const runtime = new FakeRuntime();
  runtime.registerCommand({ ...descriptor, mutatesState: false }, () => ({ output: {} }));
  const { server, refreshAnnotations } = await createHotReplMcpServer({ runtime });

  const [clientTransport, serverTransport] = InMemoryTransport.createLinkedPair();
  const client = new Client({ name: "test", version: "0.0.0" }, { capabilities: {} });
  await Promise.all([
    server.connect(serverTransport),
    client.connect(clientTransport),
  ]);

  // Before refresh: conservative defaults.
  const initial = await client.listTools();
  const initialRun = initial.tools.find((t) => t.name === "hotrepl_run");
  expect(initialRun?.annotations).toMatchObject({ destructiveHint: true, readOnlyHint: false });

  // Drive the refresh; expect a tools/list_changed notification.
  const notified = new Promise<void>((resolve) => {
    client.setNotificationHandler(ToolListChangedNotificationSchema, () => resolve());
  });
  await refreshAnnotations();
  await notified;

  const refreshed = await client.listTools();
  const refreshedRun = refreshed.tools.find((t) => t.name === "hotrepl_run");
  expect(refreshedRun?.annotations).toMatchObject({ destructiveHint: false, readOnlyHint: true });
});
```

### CLI tests

```ts
// packages/cli/test/bin-exit-codes.test.ts
test("bin exits 69 when backend unreachable", async () => {
  const result = Bun.spawnSync({
    cmd: ["node", "./dist/bin.js", "info"],
    env: { ...process.env, HOTREPL_URL: "ws://127.0.0.1:1" },
    stderr: "pipe",
    stdout: "pipe",
  });
  expect(result.exitCode).toBe(69);
  expect(new TextDecoder().decode(result.stderr)).toContain(
    "WebSocket connection failed",
  );
});

test("bin exits 64 when args are invalid", async () => {
  const result = Bun.spawnSync({
    cmd: ["node", "./dist/bin.js", "--format=invalid"],
    stderr: "pipe",
  });
  expect(result.exitCode).toBe(64);
});
```

### Integration smoke-test

After all changes:

```bash
# 1. Build all publishable packages
bun run build

# 2. Pack each into /tmp/hotrepl-pack/
mkdir -p /tmp/hotrepl-pack && rm -f /tmp/hotrepl-pack/*.tgz
for p in protocol sdk cli mcp; do
  (cd packages/$p && npm pack --pack-destination=/tmp/hotrepl-pack)
done

# 3. Install the tarballs into a clean consumer dir
mkdir -p /tmp/hotrepl-smoke && cd /tmp/hotrepl-smoke
cat > package.json <<'JSON'
{
  "name": "hotrepl-smoke",
  "private": true,
  "type": "module",
  "dependencies": {
    "@hotrepl/protocol": "file:/tmp/hotrepl-pack/hotrepl-protocol-2.0.0.tgz",
    "@hotrepl/sdk": "file:/tmp/hotrepl-pack/hotrepl-sdk-2.0.0.tgz",
    "@hotrepl/cli": "file:/tmp/hotrepl-pack/hotrepl-cli-2.0.0.tgz",
    "@hotrepl/mcp": "file:/tmp/hotrepl-pack/hotrepl-mcp-2.0.0.tgz"
  }
}
JSON
npm install --no-fund --no-audit

# 4. Drive the MCP server with a real JSON-RPC handshake and a tool call
(
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"0.0.0"}}}'
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  printf '%s\n' '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
  printf '%s\n' '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"hotrepl_eval","arguments":{"code":"1+1"}}}'
  sleep 1  # let the server respond before EOF closes stdin
) | HOTREPL_URL=ws://127.0.0.1:1 node node_modules/.bin/hotrepl-mcp
```

Expected: server stays up across all four messages. `tools/list` returns 9 tools. `hotrepl_eval`
returns `isError: true` with the actionable message. Sending SIGTERM exits the server with code 0.

### Manual verification against a live game (Ardenfall Demo)

Unit tests and the unreachable-backend smoke-test together prove the plumbing. They cannot prove the
bins behave correctly when a real Unity game with a real Mono.CSharp evaluator is on the other side
of the WebSocket. The reference live target is the Ardenfall Compendium consumer at
`~/Projects/ardenfall-compendium`, which registers five typed commands against the BepInEx host:

| Command                       | `mutatesState` | Use during verification                   |
| ----------------------------- | -------------- | ----------------------------------------- |
| `compendium.info`             | false          | Read-only smoke for typed commands        |
| `compendium.preflight`        | false          | Read-only structured output               |
| `compendium.continueFromMenu` | true           | Mutating command — drives runMutates true |
| `entity.plan`                 | false          | Read-only nested output                   |
| `entity.exportBatch`          | true           | Long-running job; use for SIGINT          |

The presence of mutating commands means Ardenfall's `runMutates` evaluates to `true`, which happens
to match the conservative defaults — so the annotation refresh's *effect* is invisible at the
protocol layer here, but the round-trip itself (the `notifications/tools/list_changed` emission)
remains observable.

**Setup (once per machine):**

```bash
cd ~/Projects/ardenfall-compendium
cp .env.example .env   # fill in ARDENFALL_MANAGED_DIR, ARDENFALL_PLUGINS_DIR,
                       # HOTREPL_REPO, HOTREPL_*_OUT
bun install --frozen-lockfile
bun run hotrepl:setup  # builds HotRepl.BepInEx + the Ardenfall mod and
                       # deploys both to BepInEx/plugins
# Launch Ardenfall Demo; either manually or, if ARDENFALL_LAUNCH_COMMAND is set:
bun run hotrepl:launch
```

The game now exposes `ws://127.0.0.1:18590` once the title screen loads.

**Verification matrix.** All commands run from `/tmp/hotrepl-smoke` (the consumer dir populated by
the integration smoke-test above), using the packed-and-installed bins — not the workspace source.
This proves the published artefact behaves correctly, not just the workspace code.

1. **MCP startup with live backend.** Drive the same JSON-RPC handshake from the smoke-test, but
   without overriding `HOTREPL_URL`. Expected:
   - `tools/list` returns 9 tools.
   - Background refresh fires before EOF; the test observes a `notifications/tools/list_changed`
     notification.
   - `tools/call` with `hotrepl_eval` (`"UnityEngine.Application.productName"`) returns a
     non-`isError` result with text including `"Ardenfall"`.
   - `tools/call` with `hotrepl_run` (`"compendium.info"`, `{}`) returns a non-`isError` result with
     the command's structured output and an empty `artifacts` map (compendium.info has no
     artifacts).
   - Server stays running after the calls (no spurious shutdown).

2. **MCP SIGTERM with active connection.** Spawn the server, send the handshake + one successful
   `tools/call`, then `kill -TERM <pid>`. Expected: process exits 0, the WebSocket transport closes
   cleanly (verifiable from BepInEx console — no warnings about an abandoned client), and
   stdin/stdout drain before exit.

3. **CLI eval against live game.**
   ```bash
   hotrepl info --json
   hotrepl eval 'UnityEngine.Application.productName'
   hotrepl run compendium.info '{}'
   ```
   Expected:
   - `info` returns the handshake JSON with `evaluator` populated.
   - `eval` prints `Ardenfall` and exits 0.
   - `run` prints the structured output and exits 0.

4. **CLI SIGINT during long-running eval.**
   ```bash
   hotrepl eval 'System.Threading.Thread.Sleep(5000); 1'
   # press Ctrl-C ~1s in
   ```
   Expected: exit code 130, no stack trace on stderr. Per the "Mono.CSharp tight-loop / Sleep" note
   in AGENTS.md, the eval cannot always be aborted; the CLI's own SIGINT handler still exits 130
   even if the underlying eval keeps running on the game side.

5. **MCP backend transition unreachable → reachable.**
   - Start `npx -y @hotrepl/mcp` with Ardenfall **not** running.
   - Send `tools/call hotrepl_eval` → expect `isError: true` with the actionable "HotRepl is not
     reachable" message.
   - Launch Ardenfall (game opens, BepInEx loads).
   - Send `tools/call hotrepl_eval` again on the same server → expect a non-`isError` result. This
     proves `SessionManager.getSession()` is lazy and retries on each call (out-of-scope
     reconnection logic is unnecessary here because no prior session existed — each tool call
     attempts a fresh connect).

6. **MCP backend transition reachable → unreachable.** Reverse of (5): start with Ardenfall running,
   make one successful call, close the game, make another call. Expected: `isError: true` with the
   "session evicted" or "connection closed" message (depending on whether the game closed cleanly or
   crashed). This is the gap covered by the deferred reconnection-on-eviction work in *Out of
   scope*; the manual test confirms today's behavior is the cleanly-degraded one (isError envelope,
   not a server crash).

**Recording results.** Each verification run goes into the implementation PR description as a short
note: command run, observed output, pass/fail. A failure on any of (1)–(4) is a regression and
blocks the merge. Failures on (5)–(6) are documented as known gaps and tracked separately.

## Out of scope

- **Reconnection on session_evicted.** Today, when the backend evicts the session, the SDK reports
  the event but the SessionManager doesn't try to reconnect. Worth fixing — separate concern.
- **MCP streaming/SSE transport.** stdio only for now.
- **CLI `--debug` flag** that shows stack traces. Could be added later; current behavior (clean
  messages always) is correct for normal users.
- **Telemetry / structured logging.** stderr logs are unstructured; that's fine for v2.0.

## Implementation order

1. Layer 1A (SDK uncaught error fix) + Layer 1B (Session.close) + tests
2. Layer 2A (sync createHotReplTools) + tests
3. Layer 2C (safeTool wrappers) + tests
4. Layer 2B (background refresh) + tests
5. Layer 2D (MCP bin) + integration test
6. Layer 3A (CLI bin) + tests
7. Re-run end-to-end smoke test

Each layer is an atomic commit per the project's commit conventions.

## Self-review notes

- All "TBD"/"TODO" placeholders removed.
- Architecture sketches match the file-change table.
- All public API additions listed in Components.
- `refreshAnnotations` returns void and is fire-and-forget; the bin doesn't await it. Verified
  consistent across the spec.
- `Session.close()` is idempotent — implementation MUST guard against double close.
- `safeTool` wrapper applied to ALL nine tool handlers, not just the invocation paths — verified by
  checking handler list.
- Conservative defaults match MCP spec defaults exactly:
  `destructiveHint: true, readOnlyHint: false`.
- Exit codes verified against sysexits.h.
- Top-level await pitfall addressed: `process.exitCode + return` instead of fire-and-forget; bin
  `try/catch` covers all paths.
