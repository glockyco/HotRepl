# HotRepl Clean Architecture Design

## Status

Design for a full reimplementation. No backward compatibility constraints. Big-bang rewrite assumed.
Solo-developer, single-agent, single-game environment.

Source-of-truth for the next implementation cycle of HotRepl, the canonical TypeScript client SDK,
the CLI, the MCP adapter, and the conventions that downstream consumers (`ardenfall-compendium`,
`ancient-kingdoms-mods`) follow.

## Problem

The current ecosystem grew organically and now carries several costs:

- Consumers manually choreograph `connect → auth → lease → describe → call → poll → result`.
  `ardenfall-compendium` reimplements a TypeScript WebSocket client because the Python client cannot
  be reused from a Bun controller.
- The protocol advertises capabilities and limits (`maxMessageBytes`, `maxQueuedCommands`,
  `jobEventsSupported`) that the runtime does not enforce or implement.
- `control_auth`, `lease_acquire`, `idempotencyKey`, and the multi-state job lifecycle
  (`accepted/running/cancelling`) add ceremony without benefit in the single-agent / single-process
  / localhost-loopback environment that HotRepl actually targets.
- `Repl.History` lives in the evaluator, so `reset` destroys it; consumers cannot get a stable
  journal.
- Two parallel client codebases (`client/src/hotrepl` and
  `ardenfall-compendium/controller/src/hotrepl-client.ts`) define overlapping types and parse the
  same protocol, drifting independently.
- Schemas are advertised but mostly empty (`AnyObject`/`EmptyObject`), so codegen and typed wrappers
  cannot exist.
- The CLI exposes both high-level (`control run`) and low-level
  (`start-job`/`job-status`/`job-result`/`cancel`) verbs as equals, even though the low-level ones
  are debug-only in practice.
- Two surfaces (CLI vs hypothetical MCP) cannot coexist gracefully because each opens a fresh
  single-client WebSocket and evicts the other.

The product itself is sound: a runtime C# REPL over WebSocket with typed commands, jobs, and
artifact references for live Unity game state. The implementation needs to catch up with what that
product asks for.

## Goals

1. **Typed commands + named artifacts are first-class.** Every repeatable workflow becomes a typed
   command with a real JSON Schema, validated on entry and on exit; results carry a named artifact
   map.
2. **Eval is co-primary with `run`.** Interactive agent eval gets the same ergonomic care as command
   execution.
3. **One canonical SDK.** TypeScript. Owns all choreography. CLI and MCP are thin adapters.
   Downstream consumers depend only on the SDK and their own typed facade.
4. **Honest capabilities.** Everything the handshake advertises is implemented and enforced.
5. **Minimum protocol surface.** Drop auth, leases, idempotency-key, job events, multi-state job
   substates, evaluator-owned history.
6. **AX-first errors.** Stable machine-readable error codes, fixed kinds, retryable flag, structured
   details.
7. **Great debug/test experience.** Server-side journal, in-process fake runtime, record/replay
   fixtures, structured logs, MCP Inspector compatibility.
8. **Preserve Unity invariants.** Main-thread execution, single active client, evaluator-honest
   cancellation semantics, no Core / host / evaluator coupling.

## Non-goals

- Remote (non-loopback) operation. The server binds 127.0.0.1.
- Multi-user / multi-tenant. One developer, one agent, one game.
- Multiple simultaneous controllers. The single-client invariant stays.
- Cross-game multiplexing. One HotRepl instance per game process.
- Cross-process job follow / reconnectable jobs. A connection drop ends the workflow.
- Continued maintenance of a Python client. (Deferred indefinitely; the protocol stays
  language-neutral so a future port is possible, but it is not maintained.)
- Generalized auth/RBAC. Authority comes from loopback bind + the single-client invariant.

## Design principles

These are restated from the architecture review and adopted verbatim.

1. **Typed commands are the product. Eval is the privileged escape hatch.** Both first-class, but
   distinct roles.
2. **One canonical SDK. Multiple surfaces.** SDK owns choreography; surfaces own ergonomics.
3. **Protocol stays narrow. SDK hides choreography.** The wire format is JSON-RPC-shaped messages;
   orchestration is SDK code.
4. **Capability negotiation, not feature flags.** Handshake declares what is supported AND enforced.
   Clients degrade gracefully.
5. **Progressive disclosure for agents.** Tool catalogs stay small; descriptors are fetched on
   demand. (Anthropic and Cloudflare both ship 90%+ token savings vs naive tool dumps.)
6. **Stable, machine-readable errors.** Every failure has a kind, code, retryable flag, optional
   structured details, and a human message.
7. **Single-client honesty.** Eviction is visible. No silent multiplexing.
8. **Server-side authority.** Schemas are validated. Limits are enforced. Journal is server-owned.

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│ HotRepl repo (the platform)                                       │
│                                                                   │
│  ─── Game side (C#) ────────────────────────────────────────────  │
│  HotRepl.Protocol               wire types + error codes          │
│  HotRepl.Runtime                kernel: session, dispatch,        │
│                                 command registry, job manager,    │
│                                 artifact store, journal, limits   │
│  HotRepl.Evaluator.Roslyn       eval/reset/complete adapter       │
│  HotRepl.Evaluator.MonoCSharp   eval/reset/complete adapter       │
│  HotRepl.Helpers / .Unity / .Il2Cpp   convenience surface         │
│  HotRepl.Host.BepInEx           bootstrap only                    │
│  HotRepl.Host.MelonLoader       bootstrap only                    │
│                                                                   │
│  ─── Client side (TS, Bun) ─────────────────────────────────────  │
│  @hotrepl/protocol              shared types + JSON Schemas       │
│  @hotrepl/sdk                   canonical Session API             │
│  @hotrepl/cli                   `hotrepl` binary                  │
│  @hotrepl/mcp                   `hotrepl-mcp` MCP server          │
│  @hotrepl/testing               in-process fake runtime           │
│                                                                   │
└──────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────┐
│ Consumer repos                                                    │
│                                                                   │
│  Game mod:    IControlCommandHandler implementations              │
│               registered with GlobalControlCommandRegistry        │
│  Controller:  TypeScript depending on @hotrepl/sdk                │
│               + a thin typed facade (e.g. CompendiumClient)       │
│                                                                   │
└──────────────────────────────────────────────────────────────────┘
```

### Component responsibilities

| Component           | Owns                                                                                                            | Does NOT own                                |
| ------------------- | --------------------------------------------------------------------------------------------------------------- | ------------------------------------------- |
| `HotRepl.Protocol`  | message records, error codes, capability schema                                                                 | transport, business logic, evaluator state  |
| `HotRepl.Runtime`   | sessions, command routing, job lifecycle, artifact metadata, journal, limit enforcement, main-thread dispatcher | Unity APIs, game state, evaluator internals |
| Evaluator adapters  | `eval`/`reset`/`complete`, cancellation/timeout capability                                                      | journal, control plane, scheduling          |
| Host adapters       | Unity lifecycle wiring, evaluator choice, transport start/stop, instance discovery doc                          | protocol logic                              |
| `@hotrepl/protocol` | wire types, error enums, capability types, JSON Schemas for built-in messages                                   | choreography                                |
| `@hotrepl/sdk`      | `Session`, auto-acquire choreography, descriptor cache, typed errors, profile/discovery resolution              | argparse, MCP plumbing                      |
| `@hotrepl/cli`      | argparse / commander, terminal vs JSON vs JSONL formatting, exit codes                                          | protocol details                            |
| `@hotrepl/mcp`      | MCP tool surface (six tools), annotations from descriptors, stdio transport                                     | choreography (uses SDK)                     |
| `@hotrepl/testing`  | in-process fake runtime, record/replay, schema validators                                                       | production logic                            |
| Consumer            | game-specific typed facades, workflow logic, domain schemas                                                     | protocol primitives                         |

## Protocol

JSON over WebSocket. UTF-8. Each message has `type` and `id`. The server sends a `handshake`
immediately on connect. The protocol is intentionally small.

### Message inventory (final, post-cuts)

```
# session lifecycle
server → client:   handshake { capabilities, limits, evaluator, host }
server → client:   session_evicted { reason: "displaced", by: { clientName } }

# eval surface
client → server:   eval { code, timeoutMs?, evaluator? }
                   complete { code, cursor? }
                   reset { }
                   subscribe { code, intervalFrames?, onChange?, limit?, timeoutMs? }
                   cancel { targetId }                   # cancels eval/subscribe by id
server → client:   eval_result | eval_error
                   complete_result
                   reset_result
                   subscribe_result | subscribe_error    # final:true terminates
                   assembly_reload                       # unsolicited

# command surface
client → server:   commands_list { since? }              # progressive disclosure
                   command_describe { name }
                   command_call { name, args, timeoutMs? }
                   job_status { jobId }
                   job_cancel { jobId }
server → client:   commands_list_result { commands, since }
                   command_describe_result { descriptor }
                   command_result            # terminal for sync command
                   job_accepted              # ack of a job command_call; job is "running" from this point
                   job_status_result
                   job_result                # terminal for job (success or failure)
                   job_cancel_result

# observability
client → server:   journal_query { kind?: "eval"|"command", limit? }
server → client:   journal_query_result { entries }
```

### What is gone

- `control_auth`, `control_auth_result` — no auth in solo-local.
- `lease_acquire`, `lease_acquire_result`, all `leaseId` fields — no leases.
- `idempotencyKey` field — no server dedupe.
- `JobEventMessage` and the event buffer — was unused, advertised unsupported.
- `select_evaluator` from the primary surface (still in the runtime as a maintenance/debug command,
  not in the public CLI/MCP).
- All multi-state job substates beyond `running → done | failed | cancelled`.

### Handshake (capabilities)

```jsonc
{
  "type": "handshake",
  "protocolVersion": 2,
  "host": { "name": "MelonLoader", "version": "0.x", "platform": "Unity IL2CPP" },
  "evaluator": {
    "name": "Roslyn.Script",
    "languageVersion": "latest",
    "persistentState": true,
    "supportsCompletion": false,
    "cancellation": "cooperative" // "cooperative" | "hardAbort" | "unsupported"
  },
  "availableEvaluators": ["Roslyn.Script", "Roslyn.Isolated"],
  "defaultUsings": ["System", "System.Linq", "HotRepl.Helpers.Unity"],
  "helpers": ["String[] Help()", "Object History(Int32 limit = 20)"],
  "control": {
    "supported": true,
    "commandsListChanged": false,
    "schemaValidation": true
  },
  "limits": {
    "maxMessageBytes": 4194304,
    "maxQueuedCommands": 32,
    "maxResultLength": 102400,
    "maxEnumerableElements": 100,
    "defaultEvalTimeoutMs": 10000,
    "maxJobConcurrency": 1
  },
  "enforces": [
    "maxMessageBytes",
    "maxQueuedCommands",
    "maxResultLength",
    "maxEnumerableElements",
    "maxJobConcurrency"
  ]
}
```

The `enforces` array makes the contract explicit: clients only need to plan for what the server
actually rejects.

### Error envelope (universal)

```jsonc
{
  "kind": "validation_failed", // see enum below
  "code": "runIdRequired", // stable, per-handler
  "message": "runId is required.",
  "retryable": false,
  "details": { "field": "runId" } // optional, schema-validated by SDK
}
```

Error kinds (closed enum, locked for v2):

```
validation_failed   precondition_failed   conflict
timeout             cancelled             busy
unknown_command     unsupported_operation
artifact_missing    invalid_request       internal
```

There is no `auth_failed` or `lease_*` kind because there is no auth or lease.

## Runtime

The C# kernel keeps the current `Tick()` drain order, formalized:

```
Tick():
  1. drain cancellations
  2. drain control commands (sync execution; jobs are dispatched async on a tick task)
  3. start ≤ maxJobConcurrency newly accepted jobs (handler runs off-main when permitted)
  4. execute ≤ 1 eval
  5. tick subscriptions
```

Substantive runtime changes:

- **Server-side journal.** Two ring buffers keyed by kind (`eval`, `command`), each entry:
  `{ id, name?, code?, success, durationMs, errorKind?, timestamp }`. Bodies excluded; payloads are
  reference-only. Exposed via `journal_query`. Survives `reset` and evaluator swaps.
- **Limit enforcement.** Inbound frames over `maxMessageBytes` are rejected with `invalid_request`
  before parsing. Once `maxQueuedCommands` items sit in the command queue, additional
  `command_call`/`command_describe`/`eval` requests are rejected with `busy`. Outbound result
  serialization truncates per `maxResultLength` and `maxEnumerableElements` (already implemented;
  just formalized).
- **Capability-declared cancellation.** Evaluator capabilities + descriptor.cancellation flow into
  runtime so the SDK can tell callers what cancellation actually does for each operation.
- **Eviction notification.** Before closing the displaced socket, the server sends
  `session_evicted { reason: "displaced", by: { clientName } }`. The SDK surfaces this as a typed
  event.

## Host adapters

Unchanged in role; trimmed in responsibility.

- `HotRepl.Host.BepInEx`: Mono runtime, uses `HotRepl.Evaluator.MonoCSharp` by default.
- `HotRepl.Host.MelonLoader`: IL2CPP/.NET 6, uses `HotRepl.Evaluator.Roslyn` by default.
- Each host: instantiate `ReplEngine`, wire `Tick()` to the Unity update loop, write the instance
  discovery JSON.
- No control commands. No protocol logic.

## Client SDK (`@hotrepl/sdk`, TypeScript)

The center of the new architecture.

### Public surface (illustrative; not the final API freeze)

```ts
import { connect, type Session, type Result, type Artifact, HotReplError } from "@hotrepl/sdk";

// connect resolves URL (default, env, profile, or explicit) and completes handshake.
await using session = await connect({ url: "ws://127.0.0.1:18590" });

// capabilities are eagerly read; SDK validates protocolVersion compatibility.
session.capabilities.evaluator.name;        // "Roslyn.Script"
session.capabilities.evaluator.cancellation;// "cooperative"

// command discovery, cached for the session lifetime.
const commands = await session.commands.list();
await session.commands.require({
  "compendium.preflight": { kind: "sync",  majorVersion: 1 },
  "entity.exportBatch":   { kind: "job",   majorVersion: 1 },
  "run.finalize":         { kind: "sync",  majorVersion: 1 },
});

// run(): the canonical command invocation.
//   sync -> resolves to Result
//   job  -> waits by default, returns Result
//   wait:false -> returns JobHandle
const result: Result = await session.run("entity.exportBatch", {
  runId, entity: "item", offset: 0, limit: 200
}, { timeoutMs: 30_000 });

result.output;                              // typed via descriptor outputSchema (TS generated)
const chunk: Artifact = result.artifacts.chunk;
const bytes = await chunk.bytes();          // hash-verified
const json  = await chunk.json();

// eval(): peer to run. Interactive agent use case.
const expr = await session.eval("Repl.Inspect(Application)", { timeoutMs: 5_000 });
expr.value;          // typed `unknown`; eval results are untyped JSON
expr.stdout;
expr.hasValue;

// watch(): subscription as an async iterable.
for await (const tick of session.watch("Player.transform.position", { intervalFrames: 30 })) {
  if (tick.kind === "value") console.log(tick.seq, tick.value);
  if (tick.kind === "error") break;
  if (tick.final) break;
}

// journal: read the server-side history.
for (const entry of await session.journal({ kind: "command", limit: 20 })) {
  // { id, name, success, durationMs, errorKind?, timestamp }
}

// graceful close via `using`/`await using` (TC39 explicit resource management).
```

### Choreography baked in

- **Single connect call.** `connect()` does TCP + handshake + capability check + protocol-version
  match; no second round trip is required before normal work.
- **No auth, no lease.** Removed from the surface entirely.
- **Auto-pick sync vs job.** `run()` consults the cached descriptor; consumers do not pick.
- **Job polling.** SDK polls every `pollIntervalMs` (default 250 ms, configurable per call); no
  consumer code touches `job_status` / `job_result`.
- **Hash-verified artifacts.** `Artifact.bytes()` / `.json()` / `.text()` re-hash and compare to
  `sha256` from the ref; throw `HotReplArtifactCorrupted` on mismatch.
- **Typed errors.** Single exception type `HotReplError` with `.kind` (closed union), `.code`,
  `.retryable`, `.details`. Helpers: `isValidation()`, `isTimeout()`, etc.
- **Descriptor cache.** `session.commands` reads `commands_list` once per session and
  `command_describe` on demand. `commandsListChanged: false` in v2 means cache is valid for the
  session lifetime.

### Code generation

- `@hotrepl/protocol` ships JSON Schemas for built-in messages and capabilities. Generated TS types
  live alongside the schemas.
- Per-game schemas (e.g. `ardenfall-compendium/mod/.../Schemas/*.json`) feed a codegen step in the
  consumer repo producing `CompendiumCommandTypes.ts`. The SDK consumes typed wrappers from the
  consumer side; the SDK itself stays game-agnostic.

### Versioning

- Protocol major version (`protocolVersion: 2` on the wire). Mismatch on connect = `connect()`
  throws `HotReplProtocolMismatch` with both versions visible.
- Command descriptors carry `majorVersion`. `commands.require` rejects mismatches.
- SDK has its own semver. Protocol-major-bump and SDK-major-bump are independent.

## CLI (`@hotrepl/cli`, distributed as a single Bun binary)

The CLI is a thin wrapper that:

- routes to the SDK,
- formats output (`text` default, `--json`, `--jsonl`),
- maps `HotReplError.kind` to fixed exit codes.

```
hotrepl info                              # capabilities + handshake summary
hotrepl wait [--commands a,b,c]           # SDK connect + optional required commands
hotrepl doctor                            # full readiness report, JSON envelope
hotrepl eval [CODE] [--file f] [--timeout ms]
hotrepl reset
hotrepl complete CODE [--cursor n]
hotrepl watch CODE [--interval frames] [--on-change] [--limit n]
hotrepl run NAME ARGS_JSON [--no-wait] [--timeout ms]
hotrepl describe [NAME]                   # listing or single descriptor
hotrepl artifacts read URI|--name NAME --result-id ID
hotrepl journal [--kind eval|command] [--limit n]

# escape hatches (debug only, not in primary docs)
hotrepl debug select-evaluator NAME
hotrepl debug job-status JOB_ID
hotrepl debug job-cancel JOB_ID
```

Removals from the surface: `start-job`, `job-status`, `job-result`, `cancel`,
`control describe/call/run/...`, `discover` (still callable as `hotrepl debug
discover`), all
profile/auth flags. The `--lease` flag is gone.

Exit code map (subset):

```
0  success
2  invalid usage / parse error
3  validation_failed
4  precondition_failed
5  busy
6  unknown_command / unsupported_operation
7  timeout
8  cancelled
9  artifact_missing / artifact_corrupted
10 internal
20 server_unreachable
21 session_evicted (caller's session was displaced mid-call)
```

## MCP server (`@hotrepl/mcp`, `hotrepl-mcp` binary)

Stdio transport by default. Streamable HTTP only if a future remote use case appears; not in v2.

Small fixed tool set (nine tools). The catalog stays static; per-game commands are discovered at
runtime through `hotrepl_list_commands` and `hotrepl_describe_command`, not registered as additional
MCP tools.

```
hotrepl_info               input: {}                      readOnly
hotrepl_eval               input: { code, timeoutMs? }    destructive
hotrepl_reset              input: {}                      destructive
hotrepl_complete           input: { code, cursor? }       readOnly
hotrepl_list_commands      input: {}                      readOnly
hotrepl_describe_command   input: { name }                readOnly
hotrepl_run                input: { name, args, timeoutMs? }   destructiveHint = descriptor.mutatesState
hotrepl_read_artifact      input: { uri }                 readOnly
hotrepl_journal            input: { kind?, limit? }       readOnly
```

`hotrepl_run` is the only tool that exposes domain commands, and it does so via `name` + `args`. We
do NOT register one MCP tool per game-side control command. That follows the current MCP ecosystem
direction (Anthropic code-execution-with-MCP, Cloudflare Code Mode): keep the static tool catalog
small, push discovery to runtime.

Annotations are filled from descriptors at runtime so MCP clients see honest `readOnlyHint` /
`destructiveHint` / `idempotentHint` / `openWorldHint` per command.

The MCP server holds **one** persistent SDK Session. If a CLI invocation evicts it, the MCP server
surfaces a `session_evicted` notification, reconnects on the next tool call, and reports the
displacement once.

## Consumer wrappers

Each consumer ships a thin typed facade. The wrapper imports `@hotrepl/sdk` and exposes domain
methods. No protocol or choreography lives in the consumer.

```ts
// ardenfall-compendium/controller/src/compendium.ts
import { type Session } from "@hotrepl/sdk";
import type {
  PreflightOutput, RunHandle, PlanOutput, ExportBatchOutput, FinalizeOutput
} from "./generated/compendium-types";

export class CompendiumClient {
  constructor(private session: Session) {}

  async preflight(): Promise<PreflightOutput> {
    return (await this.session.run("compendium.preflight", {})).output;
  }
  async beginRun(outputBaseDir: string): Promise<RunHandle> {
    return (await this.session.run("run.begin", { outputBaseDir })).output;
  }
  // …entity.plan, entity.exportBatch, run.finalize, game.quit
}
```

This replaces the ~330-line custom WebSocket client currently in
`ardenfall-compendium/controller/src/hotrepl-client.ts`.

## Game-side conventions

Mods register command handlers exactly as today, with two new requirements:

1. **Real schemas.** Every command's `argsSchema` and `resultSchema` is real JSON Schema. The
   artifact map is declared by `artifactsSchema`, a JSON Schema whose `properties` map logical
   artifact names (`"chunk"`, `"manifest"`, `"snapshot"`) to the `ArtifactRef` shape, with
   `required` listing those that are always emitted. `AnyObject` is permitted only for genuinely
   free-form fields.
2. **Named artifacts.** Results expose `Artifacts` as a `Dictionary<string, ArtifactRef>` keyed by
   logical name (e.g. `"chunk"`, `"manifest"`, `"snapshot"`). Order is no longer load-bearing.

Per-mod schemas live next to the handler:

```
mod/src/Control/
  Handlers/
    EntityExportBatchCommand.cs
  Schemas/
    entity.exportBatch.args.json
    entity.exportBatch.result.json
    entity.exportBatch.artifacts.json
```

Codegen in the consumer repo emits matching TypeScript types into `controller/src/generated/`.

## Cross-cutting concerns

### Single-client model

- Server binds 127.0.0.1 by default. Loopback bind is the security boundary.
- Single active client. New connection evicts the prior one.
- Eviction is observable: server sends `session_evicted` to the displaced socket before closing.
- Eviction protocol: SDK surfaces it as a one-shot event on the displaced session; subsequent calls
  throw `HotReplSessionEvicted`.
- If simultaneous CLI + MCP becomes a hard requirement, build a separate local broker that owns the
  one socket and multiplexes. Out of scope for v2.

### Limits and enforcement

| Limit                       | Default | Why it exists                               | Solo-local risk                                 |
| --------------------------- | ------- | ------------------------------------------- | ----------------------------------------------- |
| `maxMessageBytes` (inbound) | 4 MiB   | Reject malformed/huge frames before parsing | Accidental large `args` from controller bugs    |
| `maxResultLength`           | 100 KiB | Truncate eval result serialization          | `Repl.Inspect(Application)` returning megabytes |
| `maxEnumerableElements`     | 100     | Cap enumeration in serializer               | Same                                            |
| `maxQueuedCommands`         | 32      | Backpressure when controller floods         | Loop in consumer pushes thousands of commands   |
| `defaultEvalTimeoutMs`      | 10 000  | Wallclock budget per eval                   | Runaway eval wedging the game                   |
| `maxJobConcurrency`         | 1       | Serialize jobs through the kernel           | Multiple jobs racing main-thread access         |

Every limit is enforced by the runtime; every limit appears in `handshake.enforces[]`. Limits are
configurable per host; defaults are sized for solo-local accident protection, not for a hostile
actor.

### Schemas

- `inputSchema`, `outputSchema`, `artifactsSchema` per command.
- Validation runs on entry (`command_call.args` against `inputSchema`) and on exit (`output` against
  `outputSchema`, artifact map against `artifactsSchema`). Failure → `validation_failed`.
- Validation is on by default; can be turned off via `ReplConfig.SchemaValidation = false` for
  performance-critical games (handshake will advertise `control.schemaValidation: false` so the SDK
  knows).
- Schemas are addressable: the SDK ships a JSON-Pointer-style lookup so descriptor consumers can
  inspect a sub-schema (`commands.get("entity.exportBatch").schema("input.properties.offset")`).

### Cancellation

Cancellation semantics are explicit and per-evaluator:

- `cooperative`: cancel is observed via `CancellationToken`; handler must check.
- `hardAbort`: cancel may abort the thread; runtime state can be corrupted; handler may not run
  cleanup.
- `unsupported`: cancel is a no-op the SDK refuses to send.

`handshake.evaluator.cancellation` declares the runtime baseline. Per-command descriptors may
override with a stricter value but never a looser one.

The SDK throws `HotReplCancellationUnsupported` if a caller tries `job.cancel()` on a job whose
effective cancellation is `unsupported`.

### Capabilities, not feature flags

The handshake `control.schemaValidation`, `control.commandsListChanged`,
`evaluator.supportsCompletion`, `evaluator.persistentState`, etc. are runtime-truthful. The SDK
degrades gracefully:

- No completion support → `session.complete()` rejects with `unsupported_operation` before the round
  trip.
- `commandsListChanged: false` → descriptor cache is valid for the session.

## Debugging and testing

A first-class testing story is what makes the architecture obvious to follow.

### `@hotrepl/testing` package

- `FakeRuntime`: in-process, no WebSocket, no game. Implements every wire message. Tests for the SDK
  and for consumer code run against it.
- `MockSession`: SDK Session backed by `FakeRuntime`; identical API to real `Session`. Used for
  unit-testing CompendiumClient and similar facades.
- `SessionRecorder` / `SessionReplay`: capture a JSONL transcript of a real session, replay it as a
  fixture. Snapshot-style assertions for protocol compatibility.
- `CommandHarness` (C#): instantiate a single `IControlCommandHandler` with a mock
  `ControlCommandContext` and synthetic `JObject` args; assert results and artifacts without running
  Unity. Mirrors the existing patterns in `tests/HotRepl.Tests/Unit/` and game-side `mod-tests/`.

### Conformance test suite

- `@hotrepl/conformance`: a TS package that runs every wire-level requirement of the protocol spec
  against a target. Two targets: `FakeRuntime` (must pass) and a real C# host (CI gate). New SDK
  features add conformance tests, not just unit tests.

### Server-side journal

- `journal_query` returns recent eval and command entries, capped by ring buffer (1 024 each).
- Bodies are excluded; the journal is for debugging "what happened" without preserving sensitive
  payloads.
- CLI: `hotrepl journal --kind command --limit 50` for fast triage.

### Structured logging

- The runtime emits NDJSON lines on stderr (configurable) with correlation IDs (`requestId`,
  `jobId`).
- `hotrepl tail` consumes a logs file or stderr from a host adapter, decoded into a human-readable
  timeline.

### MCP Inspector

- `@hotrepl/mcp` is compatible with `@modelcontextprotocol/inspector`. CI exercises the inspector
  against a `FakeRuntime`-backed MCP server.

## Bad patterns to design out (in addition to weak schemas)

| Pattern observed                                            | Why it is bad                               | The clean path                         |
| ----------------------------------------------------------- | ------------------------------------------- | -------------------------------------- |
| Custom WebSocket client per consumer                        | Drifts independently; duplicates parsing    | Only `@hotrepl/sdk` opens sockets      |
| Manual `auth → lease → describe → call → poll` choreography | Easy to get wrong; per-consumer regressions | SDK auto-runs; no lease at all         |
| `dict[str, Any]` / `Record<string, unknown>` everywhere     | Loses types; agents and humans guess        | Codegen typed results from schemas     |
| Parsing artifact refs by hand                               | Cannot hash-verify; cannot stream           | `Artifact.bytes()` / `.json()`         |
| Mixing transport / orchestration / presentation in CLI      | Coupled changes; hard to test               | CLI = `@hotrepl/sdk` + formatter       |
| Multiple exception classes with different fields            | Error handling boilerplate per consumer     | One `HotReplError` with closed `.kind` |
| `--lease` flag exposed to humans                            | UX clutter; users guess when to set it      | Removed entirely                       |
| `JobEvent` machinery latent in the protocol                 | Mental overhead; misleading                 | Removed                                |
| Profile + discovery for one local game                      | Configuration cargo-cult                    | Default URL + override                 |
| Job substates (`accepted/cancelling`) leaking               | Caller has to model server lifecycle        | `running → done                        |

## What gets removed (definitive list)

- Wire messages: `control_auth`, `control_auth_result`, `lease_acquire`, `lease_acquire_result`,
  `JobEventMessage`.
- Fields: `leaseId`, `idempotencyKey`, `sessionId`, `commandsListChanged: true` mode (we declare
  `false`).
- Job substates: `accepted` (collapse into the immediate `running` transition), `cancelling`.
- Helpers: `Repl.History` (replaced by server-side journal). `Repl.Help`, `Repl.Inspect`,
  `Repl.Describe` stay.
- CLI verbs gone in v2: `status`, `profile`, `control auth`, `control lease`, `control call`,
  `control start-job`, `control job-status`, `control job-result`, `control cancel`, and the old
  `--lease` flag. `wait` and `doctor` are reintroduced at the new top level with simpler behavior.
  `discover`, `select-evaluator`, and low-level job verbs survive under `hotrepl debug`.
- Python client. Repo retains `client/` only as historical reference, removed in cleanup.

## What gets added (definitive list)

- `@hotrepl/protocol`, `@hotrepl/sdk`, `@hotrepl/cli`, `@hotrepl/mcp`, `@hotrepl/testing`,
  `@hotrepl/conformance` packages.
- `session_evicted` wire message.
- `journal_query` / `journal_query_result` wire messages.
- `commands_list` paging via `since` cursor (reserved; with `commandsListChanged: false` it is
  effectively a no-op in v2 but the message shape is forward-compatible).
- `handshake.enforces` array.
- `handshake.evaluator.cancellation` field.
- Named `artifacts` map on command results.
- `artifactsSchema` field on descriptors.
- `HotReplError` typed exception with `.kind` closed union.
- `Artifact.bytes() / .json() / .text() / .open()` with hash verification.
- Codegen step: command schemas → TS types in consumer repos.
- `hotrepl journal` and `hotrepl tail` CLI commands.

## Migration / cutover strategy

Big-bang rewrite, but staged within the rewrite to keep momentum:

1. **Foundations.** New repo layout: `packages/protocol`, `packages/sdk`, `packages/cli`,
   `packages/mcp`, `packages/testing`, `packages/conformance`. New C# `HotRepl.Protocol` library
   extracted from current Core. Establish CI matrix.
2. **Wire protocol v2.** Implement the new message set in C# (`HotRepl.Runtime`) and TS
   (`@hotrepl/protocol`). Drop legacy messages; no compatibility shim. Conformance tests pass for
   both targets.
3. **SDK.** Build `Session`, `Result`, `Artifact`, `commands.{list,require,get}`,
   `eval/reset/complete/watch`, `journal`. Wire to `@hotrepl/testing` first; only then point at real
   runtime.
4. **CLI and MCP.** Build on the SDK. Snapshot-test their text/JSON output.
5. **Consumer migration.** Port `ardenfall-compendium/controller` from its TS WebSocket client to
   `@hotrepl/sdk` + `CompendiumClient`. Port `ancient-kingdoms-mods/build-tool` from documenting
   external `hotrepl` CLI use to embedding it as a dependency where appropriate.
6. **Game-side schemas.** Per command, write real `*.json` schemas next to handlers. Update existing
   handlers in `ardenfall-compendium/mod`.
7. **Cleanup.** Delete `client/` (Python), `client/tests/`, `pyproject.toml`, and Python-only CI
   surface. Update `AGENTS.md` and `.claude/skills/hotrepl/SKILL.md` to point at the TS SDK.

Each step is independently mergeable; the v1 protocol is unsupported the moment step 2 lands. There
is no need to dual-stack.

## Risks and open questions

- **Eval ergonomics regressions.** Agents may have habits formed by the current `hotrepl eval`
  command. The new CLI keeps `hotrepl eval` with identical user-facing semantics; only flags around
  timeout and evaluator selection are tightened.
- **TS-only client may exclude future Python tooling.** Acceptable today; the protocol stays
  language-neutral so a port can happen. Conformance tests guarantee any port is a valid
  implementation.
- **Schema validation cost.** JSON Schema validation on every command call is cheap but non-zero.
  The `control.schemaValidation` capability flag lets a host disable it for tight loops. Disabled by
  default in the MelonLoader host? Probably not; benchmark before deciding.
- **Removing auth feels uncomfortable.** It is honest: loopback + single-client + single-process
  model already provides the guarantee that token-based auth pretended to give. If a future
  deployment needs cross-machine access, that is a separate design and likely a proxy/broker
  concern, not a runtime change.
- **Single-client + MCP coexistence.** If the daily workflow involves an always-on MCP session plus
  occasional CLI use, eviction churn will be a nuisance. The eviction notification + auto-reconnect
  in `@hotrepl/mcp` mitigates this. A local broker remains the proper fix when this becomes painful.
- **TS codegen quality.** Consumer ergonomics depend heavily on generated types. Pick a single JSON
  Schema → TS generator and validate it covers `oneOf`, `discriminator`, and tuples. Candidates:
  `json-schema-to-typescript`, `quicktype`.
- **Mono.CSharp staying around.** It is required for BepInEx (no Roslyn on .NET Standard 2.1 Mono).
  Its quirks (`varName * expr` parser bug, no safepoint injection for tight loops) must remain
  visible through the capability surface, not papered over.

## References

- HotRepl current architecture: `AGENTS.md`, `src/HotRepl.Core/ReplEngine.cs`,
  `docs/control-plane-protocol.md`.
- HotRepl consumer audits: previous design conversation; `ardenfall-compendium/controller/src/`,
  `ardenfall-compendium/mod/src/Control/`, `ancient-kingdoms-mods/build-tool/`,
  `ancient-kingdoms-mods/.claude/skills/hotrepl-runtime-inspection/`.
- Agent Experience: <https://workos.com/blog/agent-experience-oujuh>,
  <https://nordicapis.com/what-is-agent-experience-ax/>.
- MCP architecture: <https://modelcontextprotocol.io/docs/learn/architecture>,
  <https://modelcontextprotocol.io/specification/latest/basic/transports>,
  <https://modelcontextprotocol.io/specification/latest/server/tools>.
- MCP tool annotations: <https://blog.modelcontextprotocol.io/posts/2026-03-16-tool-annotations/>.
- Progressive disclosure for MCP: <https://www.anthropic.com/engineering/code-execution-with-mcp>,
  <https://blog.cloudflare.com/code-mode/>.
- LSP/DAP separation lessons:
  <https://code.visualstudio.com/blogs/2018/08/07/debug-adapter-protocol-website>.
- nREPL session/ops design: <https://nrepl.org/nrepl/index.html>.
- CLI vs MCP, when to use which: <https://circleci.com/blog/mcp-vs-cli/>.
