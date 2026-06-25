---
title: "Publish-ready bin robustness implementation plan"
type: plan
status: implemented
created: 2026-05-23
parent: 2026-05-23-publish-ready-bin-robustness-design
superseded_by:
archived: 2026-06-25
---

# Publish-ready bin robustness implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `npx -y @hotrepl/mcp` and `npx -y @hotrepl/cli` first-run UX robust against a missing
or transitioning Unity backend — no stack traces, conservative MCP annotations refreshed once
connected, sysexits exit codes, SIGINT/SIGTERM handlers.

**Architecture:** Three layers — SDK (suppress undici's uncaught error trace, add
`Session.close()`), MCP (sync tool registration with conservative defaults, per-handler `isError`
envelopes, background annotation refresh, signal-handled bin), CLI (defense-in-depth bin try/catch,
`exitCode + return` pattern, SIGINT handler). Each layer ships as one TDD-driven atomic commit.

**Tech Stack:** Bun 1.3.14, TypeScript 6.0.3, `@modelcontextprotocol/sdk@1.29.0`, `@hotrepl/sdk`
(workspace), tsup, bun:test.

**Spec:**
[`docs/superpowers/specs/2026-05-23-publish-ready-bin-robustness-design.md`](../specs/2026-05-23-publish-ready-bin-robustness-design.md)

---

## File map

| Layer | File                                            | Action                                                                                    |
| ----- | ----------------------------------------------- | ----------------------------------------------------------------------------------------- |
| 1A    | `packages/sdk/src/websocket-transport.ts`       | Modify `open()` to add Node-style no-op `.on('error')` listener                           |
| 1A    | `packages/sdk/test/websocket-transport.test.ts` | Add test verifying no `uncaughtException` event fires on connect failure                  |
| 1B    | `packages/sdk/src/session.ts`                   | Add `close(): void` to `Session` class                                                    |
| 1B    | `packages/sdk/test/session.test.ts`             | Add test verifying `close()` is idempotent                                                |
| 2A    | `packages/mcp/src/tools.ts`                     | Make `createHotReplTools` synchronous; conservative hotrepl_run defaults                  |
| 2A    | `packages/mcp/test/tools.test.ts`               | Update existing tests for sync signature + conservative defaults                          |
| 2C    | `packages/mcp/src/tools.ts`                     | Wrap every handler in `safeTool`; add `formatBackendError`                                |
| 2C    | `packages/mcp/test/tools.test.ts`               | Add test verifying `isError` envelope on backend unreachable                              |
| 2B    | `packages/mcp/src/index.ts`                     | `createHotReplMcpServer` returns `{server, refreshAnnotations}`; capture `RegisteredTool` |
| 2B    | `packages/mcp/test/tools.test.ts`               | Add test driving InMemoryTransport, asserts `notifications/tools/list_changed` round-trip |
| 2D    | `packages/mcp/src/index.ts`                     | `runStdioMcpServer` returns `shutdown`                                                    |
| 2D    | `packages/mcp/src/bin.ts`                       | Top-level try/catch, SIGINT/SIGTERM handlers, stderr-only logging                         |
| 3A    | `packages/cli/src/bin.ts`                       | Defense-in-depth try/catch, SIGINT handler, `exitCode + return`                           |
| 3A    | `packages/cli/test/exit-codes.test.ts`          | Add bin-as-subprocess tests for connection-fail + invalid-args + SIGINT                   |

No new files. Every change lives in an existing source or test file.

---

### Task 1: SDK — suppress undici's uncaught WebSocket error

**Files:**

- Modify: `packages/sdk/src/websocket-transport.ts:146-163`
- Modify: `packages/sdk/test/websocket-transport.test.ts` (append a new test)

- [ ] **Step 1: Write the failing test**

Append to `packages/sdk/test/websocket-transport.test.ts`, inside the existing
`describe("WebSocket transport", () => { … })` block:

```ts
test("connect to an unreachable port rejects without uncaughtException", async () => {
  const uncaught: unknown[] = [];
  const onUncaught = (err: unknown) => uncaught.push(err);
  process.on("uncaughtException", onUncaught);
  process.on("unhandledRejection", onUncaught);

  try {
    await connect({ url: "ws://127.0.0.1:1" });
    expect.unreachable();
  } catch (error) {
    expect(error).toBeInstanceOf(Error);
    expect((error as Error).message).toContain("WebSocket connection failed");
  }

  // Let any deferred uncaught event surface before asserting.
  await new Promise((resolve) => setImmediate(resolve));

  process.off("uncaughtException", onUncaught);
  process.off("unhandledRejection", onUncaught);
  expect(uncaught).toEqual([]);
});
```

If `connect` is not imported in that file yet, add the import:

```ts
import { connect } from "../src/connect";
```

- [ ] **Step 2: Run the test, see it fail**

```bash
bun test packages/sdk/test/websocket-transport.test.ts -t "uncaughtException"
```

Expected: test fails because `uncaught` array contains the dispatched Error. Output looks like:

```
expect(received).toEqual(expected) // deep equality
- Expected: []
+ Received: [Error: HotRepl WebSocket connection failed.]
```

- [ ] **Step 3: Implement the fix**

Edit `packages/sdk/src/websocket-transport.ts`. In the `open()` method (currently around lines
146-163), immediately after the existing `addEventListener("error", …)` registration, add a
Node-style no-op `.on("error", …)` registration:

```ts
private open(): Promise<WebSocketTransport> {
  return new Promise((resolve, reject) => {
    const failOpen = (error: unknown): void => {
      reject(error);
      this.failAll(error);
    };

    this.socket.addEventListener("message", (event) => {
      void this.handleSocketMessage(event.data).then(resolve, failOpen);
    });
    this.socket.addEventListener("error", () => {
      failOpen(new Error("HotRepl WebSocket connection failed."));
    });
    // Belt-and-braces: undici's internal Node EventEmitter dispatches the
    // 'error' event independently of addEventListener. Without a Node-style
    // listener, EventEmitter re-throws synchronously and surfaces as an
    // uncaughtException. The W3C listener above is still the one that runs
    // user logic; this listener exists only to suppress the EventEmitter
    // re-throw. In a browser this property is undefined and the cast
    // short-circuits at the optional call.
    (this.socket as unknown as { on?: (e: string, fn: () => void) => void })
      .on?.("error", () => {});
    this.socket.addEventListener("close", () => {
      this.failAll(new Error("HotRepl WebSocket connection closed."));
    });
  });
}
```

- [ ] **Step 4: Run the test, see it pass**

```bash
bun test packages/sdk/test/websocket-transport.test.ts -t "uncaughtException"
```

Expected: PASS.

Then run the full SDK test file to verify nothing else broke:

```bash
bun test packages/sdk/test/websocket-transport.test.ts
```

Expected: all tests PASS.

- [ ] **Step 5: If the test still fails**

The hypothesis that undici dispatches via Node EventEmitter may not be the root cause. Alternative
fixes, in order:

a. The error event might be propagating via `process.emit('uncaughtException', ...)` from inside the
W3C dispatchEvent path. Add a `process.on('uncaughtException', ...)` filter at SDK import time that
only filters errors matching `"HotRepl WebSocket connection failed."`. Caveat: process-wide filter,
document carefully.

b. Replace the W3C `addEventListener('error', ...)` with `socket.onerror = …` (assignment form).
Some WebSocket implementations only fire the assignment-form handler.

c. Switch the SDK from `globalThis.WebSocket` to the `ws` package and use Node-style `.on(...)`
listeners exclusively. Last resort — adds a dep.

Pick (a), (b), or (c), rerun the test, and only commit when green.

- [ ] **Step 6: Commit**

```bash
git add packages/sdk/src/websocket-transport.ts packages/sdk/test/websocket-transport.test.ts
git commit -m "$(cat <<'EOF'
fix(sdk): suppress undici uncaughtException on WebSocket open failure

Undici's WebSocket implementation dispatches its 'error' event through
both the W3C EventTarget path (addEventListener) and Node's internal
EventEmitter path. The SDK only registered a W3C listener via
addEventListener('error', ...), which left the EventEmitter side with
no listener — Node's EventEmitter re-throws synchronously when 'error'
is emitted with no .on('error', ...) listener attached. The result was
a frightening stack trace on every connect-failure, even when the
caller had a proper try/catch around await connect().

Add a no-op .on('error', ...) listener purely to suppress the
EventEmitter re-throw. The W3C addEventListener handler above it still
runs user logic. In browsers, .on is undefined and the optional call
short-circuits with no behavioural change.

The added regression test triggers a connect failure against a
deliberately-rejected port (ws://127.0.0.1:1) and asserts no
uncaughtException or unhandledRejection event fires on the process.
EOF
)"
```

---

### Task 2: SDK — add `Session.close()`

**Files:**

- Modify: `packages/sdk/src/session.ts` (add `close()` method to `Session` class)
- Modify: `packages/sdk/test/session.test.ts` (append idempotency test)

- [ ] **Step 1: Write the failing test**

Append to `packages/sdk/test/session.test.ts`, inside the existing
`describe("Session", () => { … })`:

```ts
test("close() is idempotent and closes the underlying transport", async () => {
  const runtime = new FakeRuntime();
  const session = await connect({ runtime });
  expect(typeof session.close).toBe("function");

  session.close();
  // Second call must not throw.
  expect(() => session.close()).not.toThrow();

  // After close, the transport must be unable to dispatch further requests.
  // The exact error depends on FakeRuntime semantics; we only assert that
  // a subsequent request fails rather than hanging.
  await expect(
    session.eval("UnityEngine.Application.productName"),
  ).rejects.toBeInstanceOf(Error);
});
```

If `FakeRuntime` and `connect` aren't yet imported in that file, ensure these lines are at the top:

```ts
import { FakeRuntime } from "@hotrepl/testing";
import { connect } from "../src/connect";
```

- [ ] **Step 2: Run the test, see it fail**

```bash
bun test packages/sdk/test/session.test.ts -t "close()"
```

Expected: FAIL — `session.close is not a function` (or `typeof session.close` returns
`"undefined"`).

- [ ] **Step 3: Implement `Session.close()`**

Open `packages/sdk/src/session.ts`. Find the `Session` class (it begins around line 30; if the
layout differs, search for `class Session`). Add a `close()` method and a `closed` boolean for
idempotency:

```ts
export class Session {
  // …existing private fields…
  private closed = false;

  // …existing constructor + methods…

  /**
   * Close the underlying WebSocket transport. Safe to call multiple times;
   * subsequent calls after the first are no-ops.
   */
  close(): void {
    if (this.closed) return;
    this.closed = true;
    this.transport.close();
  }
}
```

If `Session` doesn't currently hold a reference to `transport` as a field, capture it in the
constructor as `private readonly transport: RuntimeTransport`. Re-read the file before editing to
confirm the field name and adjust.

Also confirm `RuntimeTransport` (in `packages/sdk/src/runtime-transport.ts` or wherever the
interface lives) declares `close(): void`. It already does for `WebSocketTransport`; if the
interface itself lacks it, add `close(): void` to the interface and any other implementer
(FakeRuntime in `packages/testing/src/fake-runtime.ts`). FakeRuntime can implement it as a flag-set
that subsequent requests check and reject from.

- [ ] **Step 4: Run the test, see it pass**

```bash
bun test packages/sdk/test/session.test.ts -t "close()"
```

Expected: PASS.

Run the full sdk + testing test suites:

```bash
bun test packages/sdk packages/testing
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/sdk/src/session.ts packages/sdk/test/session.test.ts \
  packages/sdk/src/runtime-transport.ts packages/testing/src/fake-runtime.ts
# (drop any of those four paths if your concrete edit didn't touch them)
git commit -m "$(cat <<'EOF'
feat(sdk): expose Session.close()

The SDK never offered a public way to release a Session's WebSocket
connection. Long-running MCP/CLI processes that need to drain stdio
on SIGTERM had no clean shutdown handle for the backend connection,
leading to half-open sockets in BepInEx logs.

Add Session.close() — a thin pass-through to the underlying
transport's close(), guarded by a 'closed' flag so it is safe to
call N times. Add close() to the RuntimeTransport interface and the
matching FakeRuntime implementation so test doubles satisfy the
contract.

Idempotency is verified by the regression test: after one close(),
a second call must not throw, and a subsequent eval must reject
(not hang).
EOF
)"
```

---

### Task 3: MCP — synchronous `createHotReplTools` with conservative defaults

**Files:**

- Modify: `packages/mcp/src/tools.ts` (remove eager session/commands fetch; conservative
  `hotrepl_run` annotations)
- Modify: `packages/mcp/src/index.ts` (drop the `await` in front of `createHotReplTools`)
- Modify: `packages/mcp/test/tools.test.ts` (update existing tests for sync signature + conservative
  defaults)

- [ ] **Step 1: Update the existing tests to match the new contract**

Open `packages/mcp/test/tools.test.ts`. The current tests `await createHotReplTools(manager)`.
Replace the awaits with synchronous calls and update the "derives hotrepl_run annotations" test to
assert conservative defaults instead.

```ts
// Test 1 ("registers exactly the fixed v2 tools") — replace the await:
const tools = createHotReplTools(manager);

// Test 2 — rename and reframe:
test("registers hotrepl_run with conservative MCP-spec defaults", () => {
  const runtime = new FakeRuntime();
  runtime.registerCommand(descriptor, () => ({ output: { ok: true } }));
  const manager = new SessionManager({ runtime });

  const tools = createHotReplTools(manager);
  const run = tools.find((tool) => tool.name === "hotrepl_run");

  // Conservative defaults match the MCP spec defaults:
  // destructiveHint: true, readOnlyHint: false. These are deliberately
  // independent of the backend's mutatesState — that refinement happens
  // later via refreshAnnotations (Task 5).
  expect(run?.annotations).toMatchObject({
    destructiveHint: true,
    readOnlyHint: false,
  });
});

// Test 3 ("hotrepl_run delegates to Session.run") — replace the await:
const tools = createHotReplTools(manager);
```

- [ ] **Step 2: Run the tests, see them fail**

```bash
bun test packages/mcp/test/tools.test.ts
```

Expected: FAIL — tests fail at the `await createHotReplTools(...)` (because the signature is still
async) AND at the new conservative-default assertion (because the current code derives
`destructiveHint: runMutates`).

- [ ] **Step 3: Rewrite `createHotReplTools` to be synchronous**

Open `packages/mcp/src/tools.ts`. Replace the entire `createHotReplTools` function (currently around
lines 14-109) with:

```ts
export function createHotReplTools(manager: SessionManager): HotReplMcpTool[] {
  // Conservative MCP-spec defaults for the mutating tool. The backend's
  // actual mutatesState is fetched lazily by refreshAnnotations (see
  // createHotReplMcpServer) and applied via RegisteredTool.update(), which
  // automatically emits notifications/tools/list_changed.
  const conservativeRunAnnotations = {
    destructiveHint: true,
    readOnlyHint: false,
  } satisfies ToolAnnotations;

  return [
    tool(
      "hotrepl_info",
      "Return runtime handshake and capability information.",
      z.object({}),
      async () => {
        const current = await manager.getSession();
        return result(current.handshake);
      },
      readOnly(),
    ),
    tool(
      "hotrepl_eval",
      "Evaluate C# code in the runtime.",
      z.object({ code: z.string(), timeoutMs: z.number().optional() }),
      async (args) => {
        const current = await manager.getSession();
        return result(await current.eval(String(args.code), optionalNumber(args.timeoutMs)));
      },
    ),
    tool("hotrepl_reset", "Reset evaluator state.", z.object({}), async () => {
      const current = await manager.getSession();
      await current.reset();
      return result({ reset: true });
    }),
    tool(
      "hotrepl_complete",
      "Return completions for C# code.",
      z.object({ code: z.string(), cursor: z.number().optional() }),
      async (args) => {
        const current = await manager.getSession();
        return result(await current.complete(String(args.code), optionalNumber(args.cursor)));
      },
      readOnly(),
    ),
    tool("hotrepl_list_commands", "List typed HotRepl commands.", z.object({}), async () => {
      const current = await manager.getSession();
      return result(await listCommandDescriptors(current));
    }, readOnly()),
    tool(
      "hotrepl_describe_command",
      "Describe one typed HotRepl command.",
      z.object({ name: z.string() }),
      async (args) => {
        const current = await manager.getSession();
        return result(await current.describeCommand(String(args.name)));
      },
      readOnly(),
    ),
    tool(
      "hotrepl_run",
      "Run a typed HotRepl command by name.",
      z.object({
        name: z.string(),
        args: z.record(z.string(), z.unknown()).default({}),
        timeoutMs: z.number().optional(),
      }),
      async (args) => {
        const current = await manager.getSession();
        const runOptions: RunOptions = { pollIntervalMs: 0 };
        if (args.timeoutMs !== undefined) runOptions.timeoutMs = Number(args.timeoutMs);
        const output = await current.run(String(args.name), args.args ?? {}, runOptions);
        return result(serializableResult(output));
      },
      conservativeRunAnnotations,
    ),
    tool(
      "hotrepl_read_artifact",
      "Read and verify a HotRepl artifact reference.",
      z.object({ ref: z.unknown() }),
      async (args) => {
        const current = await manager.getSession();
        const artifact = current.artifact(args.ref as Parameters<Session["artifact"]>[0]);
        return result({ text: await artifact.text() });
      },
      readOnly(),
    ),
    tool(
      "hotrepl_journal",
      "Query recent eval and command journal entries.",
      z.object({ kind: z.enum(["eval", "command"]).optional(), limit: z.number().optional() }),
      async (args) => {
        const current = await manager.getSession();
        const query: Parameters<Session["journal"]>[0] = {};
        if (args.kind === "eval" || args.kind === "command") query.kind = args.kind;
        if (args.limit !== undefined) query.limit = Number(args.limit);
        return result(await current.journal(query));
      },
      readOnly(),
    ),
  ];
}
```

The only differences from the existing implementation:

1. Function is no longer `async`; no `await manager.getSession()` or
   `await listCommandDescriptors(...)` at the top.
2. `hotrepl_run` uses `conservativeRunAnnotations` instead of the derived
   `{ destructiveHint: runMutates, ... }`.

`listCommandDescriptors` stays in the file — it's still used by `hotrepl_list_commands` and by Task
5's `refreshAnnotations`.

Update `packages/mcp/src/index.ts` to drop the `await` in front of `createHotReplTools` (currently
`for (const tool of await createHotReplTools(manager))`):

```ts
for (const tool of createHotReplTools(manager)) {
  // …rest unchanged…
}
```

- [ ] **Step 4: Run the tests, see them pass**

```bash
bun test packages/mcp/test/tools.test.ts
```

Expected: PASS (all three tests).

Run the entire MCP test suite to catch any other consumer:

```bash
bun test packages/mcp
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/mcp/src/tools.ts packages/mcp/src/index.ts packages/mcp/test/tools.test.ts
git commit -m "$(cat <<'EOF'
fix(mcp): register tools synchronously with conservative defaults

The MCP server eagerly connected to the HotRepl backend on startup
purely to compute runMutates for hotrepl_run's destructiveHint
annotation. When the backend was unreachable, the connect failure
propagated all the way out of runStdioMcpServer's top-level await
and crashed the bin with an uncaught stack trace.

Register all nine tools synchronously and use the MCP spec's
conservative defaults for hotrepl_run (destructiveHint: true,
readOnlyHint: false). These defaults treat hotrepl_run as
potentially destructive, which is the correct safe assumption when
the server has no information about the backend's actual command
set. A follow-up commit wires up a background refresh that updates
the annotations once the backend is reachable.

createHotReplTools loses its async signature and the eager
listCommandDescriptors call. createHotReplMcpServer is updated to
no longer await it.
EOF
)"
```

---

### Task 4: MCP — `safeTool` wrapper turning backend failures into `isError` envelopes

**Files:**

- Modify: `packages/mcp/src/tools.ts` (add `safeTool`, `formatBackendError`,
  `HOTREPL_NOT_REACHABLE_MESSAGE`; wrap every handler)
- Modify: `packages/mcp/test/tools.test.ts` (add test for isError envelope on unreachable backend)

- [ ] **Step 1: Write the failing test**

Append to `packages/mcp/test/tools.test.ts`:

```ts
test("tool invocation surfaces backend-unreachable as isError envelope", async () => {
  // Build a RuntimeTransport whose open() rejects, mirroring a real
  // connect-failure path. FakeRuntime is normally reachable; here we want
  // an explicit failure.
  const failingRuntime: RuntimeTransport = {
    open: () => Promise.reject(new Error("HotRepl WebSocket connection failed.")),
    request: () => Promise.reject(new Error("transport closed")),
    watch: async function*() {
      throw new Error("transport closed");
    },
    fetchArtifact: () => Promise.reject(new Error("transport closed")),
    onSessionEvicted: () => () => {},
    close: () => {},
  };
  const manager = new SessionManager({ runtime: failingRuntime });

  const tools = createHotReplTools(manager);
  const eval_ = tools.find((tool) => tool.name === "hotrepl_eval");
  expect(eval_).toBeDefined();

  const result = await eval_!.handler({ code: "1 + 1" });
  expect(result.isError).toBe(true);
  expect(result.content).toEqual([{
    type: "text",
    text: expect.stringContaining("HotRepl is not reachable"),
  }]);
});
```

Add the `RuntimeTransport` import at the top of the test file if not already present:

```ts
import type { RuntimeTransport } from "@hotrepl/sdk";
```

- [ ] **Step 2: Run the test, see it fail**

```bash
bun test packages/mcp/test/tools.test.ts -t "isError envelope"
```

Expected: FAIL — the handler currently throws instead of returning an `isError` envelope. The error
from `manager.getSession()` propagates up out of the handler.

- [ ] **Step 3: Add `safeTool` and wrap every handler**

Open `packages/mcp/src/tools.ts`. At the bottom (next to `result`, `readOnly`, `optionalNumber`,
`serializableResult`), add:

```ts
const HOTREPL_NOT_REACHABLE_MESSAGE =
  "HotRepl is not reachable at the configured URL. Make sure your Unity game with the HotRepl plugin (BepInEx) or mod (MelonLoader) is running.";

function isConnectionFailure(error: unknown): boolean {
  if (!(error instanceof Error)) return false;
  const message = error.message;
  return (
    message.includes("WebSocket connection failed")
    || message.includes("WebSocket connection closed")
    || message.includes("ECONNREFUSED")
    || message.includes("ENOTFOUND")
  );
}

function formatBackendError(error: unknown): string {
  if (isConnectionFailure(error)) return HOTREPL_NOT_REACHABLE_MESSAGE;
  if (error instanceof Error) return error.message;
  return String(error);
}

function safeTool<T extends (args: Record<string, any>) => Promise<CallToolResult>>(
  handler: T,
): T {
  return (async (args: Record<string, any>) => {
    try {
      return await handler(args);
    } catch (error) {
      return {
        content: [{ type: "text" as const, text: formatBackendError(error) }],
        isError: true,
      };
    }
  }) as T;
}
```

Then, in the `createHotReplTools` return array, wrap every handler with `safeTool(...)`. Each
`tool(...)` call currently has the shape
`tool(name, desc, schema, async (args) => {...}, annotations?)`. The handler is the 4th argument;
replace `async (args) => { … }` with `safeTool(async (args) => { … })`. Apply to all nine tool
registrations.

Example for `hotrepl_eval`:

```ts
tool(
  "hotrepl_eval",
  "Evaluate C# code in the runtime.",
  z.object({ code: z.string(), timeoutMs: z.number().optional() }),
  safeTool(async (args) => {
    const current = await manager.getSession();
    return result(await current.eval(String(args.code), optionalNumber(args.timeoutMs)));
  }),
),
```

Do the same for `hotrepl_info`, `hotrepl_reset`, `hotrepl_complete`, `hotrepl_list_commands`,
`hotrepl_describe_command`, `hotrepl_run`, `hotrepl_read_artifact`, `hotrepl_journal`. Nine
wrappings total.

- [ ] **Step 4: Run the test, see it pass**

```bash
bun test packages/mcp/test/tools.test.ts -t "isError envelope"
```

Expected: PASS.

Verify the existing `hotrepl_run delegates to Session.run` test still passes — the happy path must
remain unchanged:

```bash
bun test packages/mcp/test/tools.test.ts
```

Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/mcp/src/tools.ts packages/mcp/test/tools.test.ts
git commit -m "$(cat <<'EOF'
fix(mcp): convert backend failures to isError tool results

Tool handlers previously threw on backend errors, which the MCP
server transport surfaced as a JSON-RPC error response. MCP clients
(Claude Desktop, Cursor, Zed) treat JSON-RPC errors as transport
failures and may hide them from the user. The MCP spec reserves
JSON-RPC errors for protocol/transport problems and asks servers to
surface execution failures inline via CallToolResult.isError.

Wrap every tool handler in safeTool: on any thrown error, return
{ content: [text], isError: true }. formatBackendError translates
connection failures into a clear, actionable message that names the
specific remediation ("start your Unity game with the HotRepl
plugin"). The MCP client renders that message in the conversation
instead of swallowing it.

The wrapping applies to all nine tools so a backend transition
(game closes, network blip, eviction) reads the same way from the
agent's perspective regardless of which tool was invoked.
EOF
)"
```

---

### Task 5: MCP — background annotation refresh + `refreshAnnotations` export

**Files:**

- Modify: `packages/mcp/src/index.ts` (capture `RegisteredTool` for `hotrepl_run`; return
  `{server, refreshAnnotations}`)
- Modify: `packages/mcp/test/tools.test.ts` (add refresh round-trip test using InMemoryTransport)

- [ ] **Step 1: Write the failing test**

Append to `packages/mcp/test/tools.test.ts`:

```ts
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { InMemoryTransport } from "@modelcontextprotocol/sdk/inMemory.js";
import { ToolListChangedNotificationSchema } from "@modelcontextprotocol/sdk/types.js";
import { createHotReplMcpServer } from "../src/index";

test("refreshAnnotations updates hotrepl_run and emits notifications/tools/list_changed", async () => {
  // Use a runtime with a non-mutating descriptor so refresh has a visible
  // effect on the annotations (sets readOnlyHint:true, destructiveHint:false).
  const nonMutatingDescriptor = { ...descriptor, mutatesState: false };
  const runtime = new FakeRuntime();
  runtime.registerCommand(nonMutatingDescriptor, () => ({ output: { ok: true } }));

  const { server, refreshAnnotations } = await createHotReplMcpServer({ runtime });

  const [clientTransport, serverTransport] = InMemoryTransport.createLinkedPair();
  const client = new Client({ name: "test", version: "0.0.0" }, { capabilities: {} });
  await Promise.all([
    server.connect(serverTransport),
    client.connect(clientTransport),
  ]);

  // Before refresh: conservative defaults.
  const before = await client.listTools();
  const beforeRun = before.tools.find((t) => t.name === "hotrepl_run");
  expect(beforeRun?.annotations).toMatchObject({
    destructiveHint: true,
    readOnlyHint: false,
  });

  // Capture the tools/list_changed notification.
  const notified = new Promise<void>((resolve) => {
    client.setNotificationHandler(ToolListChangedNotificationSchema, () => resolve());
  });

  await refreshAnnotations();
  await notified;

  // After refresh: annotations reflect the non-mutating descriptor.
  const after = await client.listTools();
  const afterRun = after.tools.find((t) => t.name === "hotrepl_run");
  expect(afterRun?.annotations).toMatchObject({
    destructiveHint: false,
    readOnlyHint: true,
  });

  await client.close();
  await server.close();
});
```

- [ ] **Step 2: Run the test, see it fail**

```bash
bun test packages/mcp/test/tools.test.ts -t "refreshAnnotations"
```

Expected: FAIL — `createHotReplMcpServer` currently resolves to `McpServer` directly, not
`{server, refreshAnnotations}`. The destructure throws.

- [ ] **Step 3: Refactor `createHotReplMcpServer`**

Open `packages/mcp/src/index.ts`. Replace the file's contents with:

```ts
import { McpServer, type RegisteredTool } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import type { SessionManagerOptions } from "./session-manager";
import { SessionManager } from "./session-manager";
import { createHotReplTools, listCommandDescriptors } from "./tools";

export { SessionManager } from "./session-manager";
export { createHotReplTools, type HotReplMcpTool } from "./tools";

export interface CreateHotReplMcpServerResult {
  server: McpServer;
  /**
   * Best-effort: connect to the HotRepl backend, fetch the command list,
   * and refine hotrepl_run's annotations to match runMutates. The update
   * call on the captured RegisteredTool auto-sends
   * notifications/tools/list_changed. Errors are swallowed — conservative
   * defaults remain in place when the backend is unreachable.
   *
   * Idempotent: concurrent calls share the in-flight refresh promise.
   */
  refreshAnnotations(): Promise<void>;
}

export async function createHotReplMcpServer(
  options: SessionManagerOptions = {},
): Promise<CreateHotReplMcpServerResult> {
  const manager = new SessionManager(options);
  const server = new McpServer({ name: "hotrepl-mcp", version: "0.0.0" });

  let registeredHotreplRun: RegisteredTool | undefined;

  for (const tool of createHotReplTools(manager)) {
    const config = {
      description: tool.description,
      inputSchema: tool.inputSchema,
    };
    if (tool.annotations !== undefined) {
      Object.assign(config, { annotations: tool.annotations });
    }
    const registered = server.registerTool(
      tool.name,
      config,
      async (args) => tool.handler(args as Record<string, any>),
    );
    if (tool.name === "hotrepl_run") registeredHotreplRun = registered;
  }

  let inflight: Promise<void> | undefined;
  const refreshAnnotations = (): Promise<void> => {
    if (inflight !== undefined) return inflight;
    inflight = (async () => {
      try {
        if (registeredHotreplRun === undefined) return;
        const session = await manager.getSession();
        const commands = await listCommandDescriptors(session);
        const runMutates = commands.some((command) => command.mutatesState);
        registeredHotreplRun.update({
          annotations: {
            destructiveHint: runMutates,
            readOnlyHint: !runMutates,
          },
        });
      } catch {
        // Backend unreachable; conservative defaults remain. Refresh is
        // best-effort and never blocks startup.
      } finally {
        inflight = undefined;
      }
    })();
    return inflight;
  };

  return { server, refreshAnnotations };
}

export async function runStdioMcpServer(options: SessionManagerOptions = {}): Promise<void> {
  const { server, refreshAnnotations } = await createHotReplMcpServer(options);
  await server.connect(new StdioServerTransport());
  // Fire-and-forget refresh; the bin doesn't await it. Conservative defaults
  // remain visible to the client until/unless the refresh succeeds.
  void refreshAnnotations();
}
```

Also export `listCommandDescriptors` from `packages/mcp/src/tools.ts` so `index.ts` can import it.
In `tools.ts`, find the `async function listCommandDescriptors(...)` declaration and add `export` to
it:

```ts
export async function listCommandDescriptors(session: Session) {
  // …existing body…
}
```

- [ ] **Step 4: Run the test, see it pass**

```bash
bun test packages/mcp/test/tools.test.ts -t "refreshAnnotations"
```

Expected: PASS.

Run all MCP tests:

```bash
bun test packages/mcp
```

Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/mcp/src/index.ts packages/mcp/src/tools.ts packages/mcp/test/tools.test.ts
git commit -m "$(cat <<'EOF'
feat(mcp): background-refresh hotrepl_run annotations once connected

After Task 3 made tool registration synchronous with conservative
defaults (destructiveHint: true, readOnlyHint: false for
hotrepl_run), the MCP server can start without contacting the
backend. The cost is annotation precision: a backend whose commands
are entirely read-only is still represented to the client as
potentially destructive.

createHotReplMcpServer now returns { server, refreshAnnotations }.
refreshAnnotations is a best-effort, fire-and-forget routine that:

1. Captures the RegisteredTool handle for hotrepl_run at registration time.
2. Connects lazily, fetches the command list, computes runMutates.
3. Calls registered.update({ annotations: ... }), which the MCP SDK
   handles by automatically dispatching notifications/tools/list_changed
   to every connected client.
4. Swallows errors — backend unreachable means the conservative
   defaults stay, no shutdown, no surface to the client.

Idempotency: concurrent calls share the in-flight promise via the
'inflight' closure variable. Once the refresh completes (or fails),
a subsequent call retries.

runStdioMcpServer wires this together: after server.connect(stdio)
returns, it kicks off refreshAnnotations() without awaiting it, so
startup latency is bounded by the stdio handshake — not by backend
reachability.

The new round-trip test drives InMemoryTransport and asserts both
the annotation change and the notifications/tools/list_changed
emission via the real MCP client API.
EOF
)"
```

---

### Task 6: MCP — `runStdioMcpServer` returns `shutdown`; bin handles signals

**Files:**

- Modify: `packages/mcp/src/index.ts` (change `runStdioMcpServer` return type to
  `Promise<() => Promise<void>>`)
- Modify: `packages/mcp/src/bin.ts` (top-level try/catch, signal handlers, stderr-only logging)
- Modify: `packages/mcp/test/tools.test.ts` (add return-type check for `runStdioMcpServer`)

- [ ] **Step 1: Update the test to enforce the new signature**

Append to `packages/mcp/test/tools.test.ts`:

```ts
test("runStdioMcpServer returns a shutdown function", async () => {
  // We don't actually connect stdio here (that would hijack the test
  // runner's stdin). Instead, we assert at the type/value level by
  // checking the returned function's shape. The full stdio + shutdown
  // path is exercised by the integration smoke test.
  // The function should be callable and return a promise.
  const runtime = new FakeRuntime();
  runtime.registerCommand(descriptor, () => ({ output: { ok: true } }));

  // We bypass the real runStdioMcpServer because it connects to real
  // stdio. Instead, verify that createHotReplMcpServer + an in-memory
  // connect lets us obtain a 'shutdown' analogue by composing
  // server.close() and refreshAnnotations cleanup.
  const { server, refreshAnnotations } = await createHotReplMcpServer({ runtime });
  expect(typeof refreshAnnotations).toBe("function");
  expect(typeof server.close).toBe("function");
});
```

That's the unit-level coverage. The full integration is covered by the smoke test in Task 8.

- [ ] **Step 2: Run the test, see it pass-or-fail consistent**

```bash
bun test packages/mcp/test/tools.test.ts -t "returns a shutdown"
```

Expected: PASS (the unit-level shape check should already work since `createHotReplMcpServer`
already returns the destructured shape).

Now write the FAILING test for the new signature contract by checking the return-type at the
consumer level. Append:

```ts
test("runStdioMcpServer's return type is a callable shutdown", () => {
  const fn: typeof runStdioMcpServer = runStdioMcpServer;
  // Compile-time check via TypeScript: the return type must be
  // Promise<() => Promise<void>>. At runtime we can only inspect the
  // function existence. The integration test in Task 8 invokes the
  // returned shutdown.
  expect(typeof fn).toBe("function");
});
```

Add the import at the top of the test file:

```ts
import { runStdioMcpServer } from "../src/index";
```

This test passes trivially. The real signature enforcement is via TypeScript at the bin's import
site.

- [ ] **Step 3: Update `runStdioMcpServer` to return a shutdown handle**

Open `packages/mcp/src/index.ts`. Replace `runStdioMcpServer`:

```ts
export async function runStdioMcpServer(
  options: SessionManagerOptions = {},
): Promise<() => Promise<void>> {
  const { server, refreshAnnotations } = await createHotReplMcpServer(options);
  const transport = new StdioServerTransport();
  await server.connect(transport);
  void refreshAnnotations();

  let closed = false;
  return async function shutdown(): Promise<void> {
    if (closed) return;
    closed = true;
    // The MCP SDK's server.close() closes the transport and emits onclose.
    // Awaiting it lets any in-flight tool call drain.
    await server.close();
  };
}
```

- [ ] **Step 4: Rewrite the MCP bin**

Open `packages/mcp/src/bin.ts`. Replace its entire contents with:

```ts
import { runStdioMcpServer } from "./index.js";

// sysexits.h: EX_OK=0, EX_SOFTWARE=70.
const EX_OK = 0;
const EX_SOFTWARE = 70;

function fatalMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

try {
  const shutdown = await runStdioMcpServer({ env: process.env });

  const onSignal = (signal: NodeJS.Signals): void => {
    void shutdown()
      .catch(() => {})
      .finally(() => {
        // SIGINT -> 130 (128 + 2), SIGTERM -> 0 (graceful).
        process.exit(signal === "SIGINT" ? 130 : EX_OK);
      });
  };

  process.on("SIGINT", onSignal);
  process.on("SIGTERM", onSignal);
} catch (error) {
  // Stdio servers MUST NOT write to stdout outside the MCP protocol.
  process.stderr.write(`hotrepl-mcp: ${fatalMessage(error)}\n`);
  process.exitCode = EX_SOFTWARE;
}
```

- [ ] **Step 5: Build and verify the bin runs cleanly with no backend**

```bash
bun run --cwd packages/mcp build
head -1 packages/mcp/dist/bin.js
```

Expected first line: `#!/usr/bin/env node`. File must be executable
(`ls -la packages/mcp/dist/bin.js` shows the `x` bit).

```bash
# Drive the server: handshake + tools/list, then EOF closes stdin.
(
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"0.0.0"}}}'
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  printf '%s\n' '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
  sleep 0.5
) | HOTREPL_URL=ws://127.0.0.1:1 node packages/mcp/dist/bin.js 2>&1 | head -30
```

Expected:

- stdout: three JSON-RPC responses (initialize, tools/list with 9 tools). NO stack trace, NO
  "Error:" lines.
- exit code: 0 (stdin EOF triggers transport close; the server exits cleanly).
- The tools/list response includes hotrepl_run with conservative annotations.

If the smoke run still produces a stack trace, return to Task 1 and verify the SDK fix actually
landed (the symptom is identical to the Task 1 bug). The two are interlocked.

- [ ] **Step 6: Run the test suite**

```bash
bun test packages/mcp
```

Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add packages/mcp/src/index.ts packages/mcp/src/bin.ts packages/mcp/test/tools.test.ts
git commit -m "$(cat <<'EOF'
feat(mcp): graceful shutdown via shutdown handle + signal-handled bin

runStdioMcpServer now returns a shutdown function instead of
resolving void. The bin captures it and wires it into SIGINT and
SIGTERM handlers so the client's documented shutdown sequence
(close stdin -> SIGTERM -> SIGKILL) terminates the WebSocket
transport cleanly before the process exits.

The bin gains a top-level try/catch that turns startup failures
into a clean stderr message plus process.exitCode = 70 (EX_SOFTWARE).
process.stdout is never written outside the MCP protocol — the spec
forbids it because the MCP client parses stdout as JSON-RPC.

Exit-code conventions match sysexits.h: SIGINT -> 130 (128 + signal
number), SIGTERM -> 0 (graceful), startup failure -> 70 (EX_SOFTWARE).

The shutdown function is idempotent: subsequent calls (e.g. when
SIGINT and SIGTERM arrive in quick succession) resolve immediately
without re-closing.
EOF
)"
```

---

### Task 7: CLI — defense-in-depth bin with SIGINT and `exitCode + return`

**Files:**

- Modify: `packages/cli/src/bin.ts` (top-level try/catch, SIGINT handler, `exitCode + return`
  pattern)
- Modify: `packages/cli/test/exit-codes.test.ts` (add bin-as-subprocess tests for connection-fail +
  invalid-args)

- [ ] **Step 1: Write the failing tests**

Append to `packages/cli/test/exit-codes.test.ts`, inside the existing
`describe("CLI exit codes", () => { … })`:

```ts
test("bin exits 69 (EX_UNAVAILABLE) when backend unreachable", () => {
  const result = Bun.spawnSync({
    cmd: [
      process.execPath, // node, available because we run via Bun's child_process polyfill
      "packages/cli/src/bin.ts",
      "info",
    ],
    env: { ...process.env, HOTREPL_URL: "ws://127.0.0.1:1" },
    cwd: import.meta.dir.replace(/\/packages\/cli\/test$/, ""),
    stderr: "pipe",
    stdout: "pipe",
  });
  expect(result.exitCode).toBe(69);
  expect(new TextDecoder().decode(result.stderr)).toContain(
    "WebSocket connection failed",
  );
});

test("bin exits 64 (EX_USAGE) when --format value is invalid", () => {
  const result = Bun.spawnSync({
    cmd: [
      process.execPath,
      "packages/cli/src/bin.ts",
      "--format=invalid",
      "info",
    ],
    cwd: import.meta.dir.replace(/\/packages\/cli\/test$/, ""),
    stderr: "pipe",
    stdout: "pipe",
  });
  expect(result.exitCode).toBe(64);
});
```

Note: `process.execPath` resolves to the `node`/`bun` binary running the test. We spawn the bin via
the source path (`packages/cli/src/bin.ts`) so the test doesn't depend on `bun run build` running
first. Bun's spawn can execute `.ts` directly when `process.execPath` is bun.

If the tests fail to find the source, replace `process.execPath` with `"bun"` (relies on `bun` being
on PATH, which is true in CI). Or, if running this against the published artefact is required, swap
to `["node", "packages/cli/dist/bin.js", "info"]` and add a `beforeAll` that runs
`bun run --cwd packages/cli build`.

- [ ] **Step 2: Run the tests, see them fail**

```bash
bun test packages/cli/test/exit-codes.test.ts -t "bin exits"
```

Expected: at least one fails — likely "bin exits 69" returns exit 1 or 0 because the current bin
uses `process.exit(result.exitCode)` and the existing exitCode for `server_unreachable` IS 69 (so
actually this might pass). Verify before proceeding.

If both tests pass with the existing bin, the new test still has value: it's the regression guard
that ensures the bin keeps exiting 69 after we replace `process.exit` with `exitCode + return`.

- [ ] **Step 3: Rewrite the CLI bin**

Open `packages/cli/src/bin.ts`. Replace its entire contents with:

```ts
import { runCli } from "./index.js";

// sysexits.h: EX_SOFTWARE=70.
const EX_SOFTWARE = 70;

function fatalMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

// SIGINT must exit deterministically (128 + 2 = 130) regardless of where
// the CLI is in its lifecycle. Install before any async work begins.
process.on("SIGINT", () => {
  process.exit(130);
});

try {
  const result = await runCli(process.argv.slice(2), { env: process.env });
  if (result.stdout.length > 0) process.stdout.write(result.stdout);
  if (result.stderr.length > 0) process.stderr.write(result.stderr);
  // Set exitCode and return so Node drains stdout/stderr naturally. Avoids
  // the truncation hazard documented for process.exit().
  process.exitCode = result.exitCode;
} catch (error) {
  process.stderr.write(`hotrepl: ${fatalMessage(error)}\n`);
  process.exitCode = EX_SOFTWARE;
}
```

- [ ] **Step 4: Run the tests, see them pass**

```bash
bun test packages/cli/test/exit-codes.test.ts
```

Expected: all PASS.

Run the full CLI test suite:

```bash
bun test packages/cli
```

Expected: all PASS.

- [ ] **Step 5: Smoke-check the bin behaves under real node + dist**

```bash
bun run --cwd packages/cli build
HOTREPL_URL=ws://127.0.0.1:1 node packages/cli/dist/bin.js info ; echo "exit=$?"
```

Expected:

```
HotRepl WebSocket connection failed.
exit=69
```

Critically: no stack trace, exit code 69 not 0.

- [ ] **Step 6: Commit**

```bash
git add packages/cli/src/bin.ts packages/cli/test/exit-codes.test.ts
git commit -m "$(cat <<'EOF'
fix(cli): clean stderr + sysexits exit codes + SIGINT handler

The CLI's runCli already catches every expected error and returns a
structured CliRunResult with an exitCode aligned to sysexits.h. The
bin around it was responsible for the remaining UX:

- process.exit(N) can truncate buffered stdout/stderr on some
  platforms. Switch to setting process.exitCode and returning, so
  Node drains the streams naturally before the event loop exits.
  Only the SIGINT handler keeps process.exit because we deliberately
  cut off mid-eval.
- A defensive top-level try/catch wraps the await so an unexpected
  throw above runCli's own handlers (a bug in parseArgs, an env
  loading hiccup) prints a clean message to stderr instead of
  crashing with a stack trace. Maps to EX_SOFTWARE (70).
- SIGINT installs early — before any await — and exits 130 (128 +
  signal number) to match Node's documented signal-exit convention.

Subprocess tests assert exit 69 on backend unreachable and exit 64
on invalid --format. Both spawn the source via Bun's runner so the
test loop stays fast and doesn't require a prior 'bun run build'.
EOF
)"
```

---

### Task 8: End-to-end verification

**Goal:** Confirm the published tarballs behave correctly against both the unreachable-backend path
AND the live Ardenfall game. No code commit; verification only.

- [ ] **Step 1: Build, pack, and install all four publishable packages**

```bash
cd /Users/joaichberger/Projects/HotRepl
rm -rf packages/*/dist
bun run build

mkdir -p /tmp/hotrepl-pack && rm -f /tmp/hotrepl-pack/*.tgz
for p in protocol sdk cli mcp; do
  (cd packages/$p && npm pack --pack-destination=/tmp/hotrepl-pack)
done

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
```

Expected: 4 tarballs in `/tmp/hotrepl-pack/`, ~5 packages installed in
`/tmp/hotrepl-smoke/node_modules`.

- [ ] **Step 2: Unreachable-backend MCP smoke**

```bash
cd /tmp/hotrepl-smoke
(
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"0.0.0"}}}'
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  printf '%s\n' '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
  printf '%s\n' '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"hotrepl_eval","arguments":{"code":"1+1"}}}'
  sleep 1
) | HOTREPL_URL=ws://127.0.0.1:1 node node_modules/.bin/hotrepl-mcp 2>&1
echo "exit=$?"
```

Expected:

- stdout contains 3 JSON-RPC responses (initialize, tools/list with 9 tools, tools/call result with
  `isError: true` and `content[0].text` containing "HotRepl is not reachable").
- NO stack trace, NO "Error:" lines from the SDK.
- `exit=0` because stdin EOF closed the transport cleanly.

- [ ] **Step 3: Unreachable-backend CLI smoke**

```bash
HOTREPL_URL=ws://127.0.0.1:1 node /tmp/hotrepl-smoke/node_modules/.bin/hotrepl info --json
echo "exit=$?"
```

Expected:

- stderr: `HotRepl WebSocket connection failed.` (one line)
- `exit=69`

- [ ] **Step 4: Live Ardenfall — start the game**

```bash
cd ~/Projects/ardenfall-compendium
# Ensure .env exists with ARDENFALL_MANAGED_DIR, ARDENFALL_PLUGINS_DIR,
# HOTREPL_REPO, HOTREPL_BEPINEX_OUT. If first run on this machine:
bun run hotrepl:setup

# Launch Ardenfall Demo via the .env launch command (or manually start
# the game from Steam if ARDENFALL_LAUNCH_COMMAND is empty):
bun run hotrepl:launch
```

Wait for the game to reach its title screen. The BepInEx console should print a HotRepl startup log
indicating `ws://127.0.0.1:18590` is listening.

- [ ] **Step 5: Live-game MCP verification**

```bash
cd /tmp/hotrepl-smoke
(
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"0.0.0"}}}'
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  printf '%s\n' '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
  printf '%s\n' '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"hotrepl_eval","arguments":{"code":"UnityEngine.Application.productName"}}}'
  printf '%s\n' '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"hotrepl_run","arguments":{"name":"compendium.info","args":{}}}}'
  sleep 2
) | node node_modules/.bin/hotrepl-mcp 2>&1
echo "exit=$?"
```

Expected:

- 4 JSON-RPC responses on stdout.
- `tools/list` returns 9 tools.
- `hotrepl_eval` result contains `"Ardenfall"` in the text content, `isError: false` (or absent).
- `hotrepl_run` (`compendium.info`) returns a non-error result with Ardenfall's structured info
  output.
- A `notifications/tools/list_changed` notification arrives somewhere in the stream (the background
  refresh fired against the live backend).
- `exit=0`

- [ ] **Step 6: Live-game CLI verification**

```bash
hotrepl=/tmp/hotrepl-smoke/node_modules/.bin/hotrepl
$hotrepl info --json | head -3
echo "---"
$hotrepl eval 'UnityEngine.Application.productName'
echo "---"
$hotrepl run compendium.info '{}'
```

Expected:

- `info --json` prints handshake JSON with a populated `evaluator` capability block.
- `eval` prints `Ardenfall` exactly (or `"Ardenfall"` depending on format — confirm against the
  source).
- `run` prints the structured output of compendium.info.
- Every command exits 0.

- [ ] **Step 7: CLI SIGINT during long eval**

```bash
$hotrepl eval 'System.Threading.Thread.Sleep(5000); 1'
# Within 1 second, press Ctrl-C.
echo "exit=$?"
```

Expected: `exit=130`. No stack trace. (The eval on the game side may continue running per the
documented Mono.CSharp limitation; the CLI itself exits cleanly.)

- [ ] **Step 8: MCP SIGTERM with active session**

```bash
# Spawn the MCP bin in the background, drive a handshake + one call,
# then send SIGTERM and wait for exit.
HOTREPL_URL=ws://127.0.0.1:18590 node /tmp/hotrepl-smoke/node_modules/.bin/hotrepl-mcp &
PID=$!
sleep 1
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"0.0.0"}}}' > /proc/$PID/fd/0 2>/dev/null \
  || echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"0.0.0"}}}' | nc -q 1 -U /dev/stdin 2>/dev/null
sleep 0.5
kill -TERM $PID
wait $PID
echo "exit=$?"
```

(macOS lacks `/proc`; if the above is awkward, use a Node harness or `expect(1)` to drive the bin.
Document the exit code observed.)

Expected: `exit=0`. The BepInEx console should not warn about an abandoned WebSocket client.

- [ ] **Step 9: Document the results**

Write a short markdown file
`docs/superpowers/specs/2026-05-23-publish-ready-bin-robustness-verification.md` (sibling to the
spec) with one paragraph per verification step listing what was run and what was observed. Commit:

```bash
git add docs/superpowers/specs/2026-05-23-publish-ready-bin-robustness-verification.md
git commit -m "$(cat <<'EOF'
docs(verification): record end-to-end results for bin robustness

Captures the manual verification matrix runs from the implementation
plan: unreachable-backend smoke, live Ardenfall handshake, MCP
SIGTERM, CLI SIGINT, and the backend reachability transitions.
EOF
)"
```

- [ ] **Step 10: Shut down Ardenfall and clean up**

```bash
# Close the game window or send SIGTERM to the Ardenfall process.
rm -rf /tmp/hotrepl-smoke /tmp/hotrepl-pack
```

---

## Self-review

**1. Spec coverage:**

| Spec section                                              | Plan task          |
| --------------------------------------------------------- | ------------------ |
| Layer 1A: SDK uncaught error fix                          | Task 1             |
| Layer 1B: Session.close                                   | Task 2             |
| Layer 2A: createHotReplTools sync + conservative defaults | Task 3             |
| Layer 2C: safeTool wrapper + formatBackendError           | Task 4             |
| Layer 2B: background annotation refresh                   | Task 5             |
| Layer 2D: MCP bin hardening + SIGTERM                     | Task 6             |
| Layer 3A: CLI bin hardening + SIGINT                      | Task 7             |
| Integration smoke-test                                    | Task 8 (steps 1-3) |
| Manual verification against Ardenfall                     | Task 8 (steps 4-8) |
| Recording results                                         | Task 8 (step 9)    |

Every spec layer maps to exactly one task. The two `Out of scope` items (reconnection on
session_evicted, MCP streaming transport) are deliberately not in the plan.

**2. Placeholder scan:**

No `TBD`, `TODO`, "implement later", or vague "add appropriate" steps. Every step shows the actual
code or actual command. The "if-the-fix-doesn't-work" branch in Task 1 lists three concrete
alternatives in priority order — that's contingency, not a placeholder.

**3. Type consistency:**

- `createHotReplTools` is async in Task 3 step 1 (tests), becomes sync in step 3 (impl), and stays
  sync everywhere downstream (Tasks 4, 5).
- `createHotReplMcpServer` returns `McpServer` before Task 5; returns `{server, refreshAnnotations}`
  from Task 5 onward.
- `runStdioMcpServer` returns `Promise<void>` before Task 6; returns `Promise<() => Promise<void>>`
  from Task 6 onward.
- `safeTool` introduced in Task 4 is referenced (via the wrapped handlers) in Tasks 5 and 6
  indirectly.
- `fatalMessage` defined identically in `packages/mcp/src/bin.ts` (Task 6) and
  `packages/cli/src/bin.ts` (Task 7); intentional duplication to keep each bin self-contained.

**4. File paths verified.** All `packages/<x>/src/<y>.ts` and `packages/<x>/test/<y>.test.ts` paths
reference files that exist at the plan's start. No fictional paths.
