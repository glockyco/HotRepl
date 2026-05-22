# HotRepl Clean Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace HotRepl v1 with the v2 clean architecture from
`docs/superpowers/specs/2026-05-22-hotrepl-clean-architecture-design.md`: a narrow C#
protocol/runtime, a canonical TypeScript SDK, thin CLI/MCP adapters, honest capabilities,
server-side journal, typed commands, named artifacts, and no Python client.

**Architecture:** Implement the HotRepl repo first as the platform boundary: C# protocol/runtime
stays Unity-safe and main-thread-driven; TypeScript packages own client choreography and testing.
Consumer repo migrations are separate plan files created in those repos once the HotRepl packages
expose the SDK contracts they consume.

**Tech Stack:** C# `netstandard2.1` libraries with Newtonsoft.Json and xUnit tests; Bun workspaces
for TypeScript packages; TypeBox for protocol schemas; `json-schema-to-typescript` plus Ajv for
command-schema consumption; MCP TypeScript SDK for stdio tools.

---

## Plan maintenance rules

- Update this file in the same commit as the code whenever implementation intentionally deviates
  from the approved spec or from this plan.
- Keep task checkboxes truthful: check a step only after the command or edit actually happened.
- Every implementation commit is atomic: one concept, one passing targeted verification command.
- Do not delete the Python client until the v2 CLI and SDK cover the behavior listed in Task 8.
- Commit messages use the repo convention `type(scope): imperative summary`.

## File Structure

### C# platform

- Create `src/HotRepl.Protocol/HotRepl.Protocol.csproj`: independent wire-type library,
  `netstandard2.1`, Newtonsoft.Json only.
- Create `src/HotRepl.Protocol/MessageType.cs`: public v2 wire discriminants.
- Create `src/HotRepl.Protocol/ErrorKind.cs`: closed v2 error-kind constants.
- Create `src/HotRepl.Protocol/Errors/HotReplErrorEnvelope.cs`: universal error envelope.
- Create `src/HotRepl.Protocol/Capabilities/*.cs`: handshake capability records.
- Create `src/HotRepl.Protocol/Messages/Inbound/*.cs` and `Messages/Outbound/*.cs`: one public
  record per v2 message.
- Create `src/HotRepl.Protocol/Serialization/ProtocolMessageSerializer.cs`: JSON serializer and
  `type` parser.
- Modify `src/HotRepl.Core/HotRepl.Core.csproj`: reference `HotRepl.Protocol`; keep
  transport/runtime logic in Core.
- Modify `src/HotRepl.Core/ReplConfig.cs`: replace control-plane-auth/lease knobs with v2 limits and
  schema-validation config.
- Modify `src/HotRepl.Core/ReplEngine.cs`: drive v2 route handling, journal writes, limit
  enforcement, job start ordering, and handshake creation.
- Modify `src/HotRepl.Core/Server/ClientRegistry.cs`: send `session_evicted` before closing
  displaced clients.
- Modify `src/HotRepl.Core/Server/MessageRouter.cs`: reject legacy message types and route the v2
  message inventory only.
- Modify `src/HotRepl.Core/Control/*.cs`: remove auth/lease/session ceremony, add named artifacts
  and `artifactsSchema`, validate command input/output/artifacts when enabled.
- Create `src/HotRepl.Core/Journal/*.cs`: ring-buffer journal for eval and command entries.
- Modify `tests/HotRepl.Tests/HotRepl.Tests.csproj`: reference `HotRepl.Protocol`.
- Create and modify focused xUnit tests under `tests/HotRepl.Tests/Unit/` for protocol round-trips,
  handshake truthfulness, eviction, journal, limits, routing, schema validation, jobs, and cleanup.

### TypeScript workspace

- Create root `package.json`: Bun workspace scripts and package manager pin.
- Create root `tsconfig.json`: strict TS config shared by packages.
- Create `packages/protocol/`: TypeBox schema source, generated JSON schemas, type exports,
  validator helpers, and tests.
- Create `packages/testing/`: `FakeRuntime`, `MockSession`, `SessionRecorder`, and `SessionReplay`
  for SDK/consumer tests.
- Create `packages/sdk/`: `connect`, `Session`, `Result`, `Artifact`, `HotReplError`, command
  descriptor cache, job polling, watch, journal, and artifact hash verification.
- Create `packages/cli/`: `hotrepl` Bun binary, command parser, formatters, and exit-code mapping.
- Create `packages/mcp/`: `hotrepl-mcp` stdio server with the nine fixed tools.
- Create `packages/conformance/`: protocol conformance suite that can target `FakeRuntime` and the
  C# host.
- Modify `lefthook.yml`: replace Python gates with Bun/TS gates after Task 8 removes `client/`.
- Modify `dprint.json`, `_typos.toml`, `AGENTS.md`, and README/docs as package layout and commands
  change.

### Out-of-repo consumer follow-up

- Create a separate Ardenfall plan in `ardenfall-compendium` before changing that repo: delete
  `controller/src/hotrepl-client.ts`, add `CompendiumClient`, run command schema codegen, and port
  orchestrator code to `@hotrepl/sdk`.
- Create a separate Ancient Kingdoms plan in `ancient-kingdoms-mods` before changing that repo: add
  typed HotRepl command handlers, rewrite `build-tool export`, delete `AutoExporter`/sentinel flow,
  and update AK docs/skills.

---

## Task 1: Repository and package foundations

**Files:**

- Create: `package.json`
- Create: `tsconfig.json`
- Create: `packages/protocol/package.json`
- Create: `packages/protocol/tsconfig.json`
- Create: `packages/protocol/src/index.ts`
- Create: `packages/protocol/src/message-types.ts`
- Create: `packages/protocol/src/error-kinds.ts`
- Create: `packages/protocol/src/handshake.ts`
- Create: `packages/protocol/scripts/export-schemas.ts`
- Modify: `.gitignore`
- Create: `packages/protocol/test/handshake.test.ts`
- Modify: `dprint.json`
- Modify: `docs/superpowers/plans/2026-05-22-hotrepl-clean-architecture-implementation.md`

- [x] **Step 1: Write the first protocol package tests**

Create `packages/protocol/test/handshake.test.ts` with assertions for the v2 protocol version,
enforced limits, closed error kinds, and TypeBox-derived validation:

```ts
import { Value } from "@sinclair/typebox/value";
import { describe, expect, test } from "bun:test";
import {
  defaultLimits,
  ERROR_KINDS,
  type HandshakeMessage,
  HandshakeMessageSchema,
  MESSAGE_TYPES,
  PROTOCOL_VERSION,
} from "../src";

describe("protocol foundations", () => {
  test("exports the locked v2 constants", () => {
    expect(PROTOCOL_VERSION).toBe(2);
    expect(MESSAGE_TYPES.handshake).toBe("handshake");
    expect(MESSAGE_TYPES.sessionEvicted).toBe("session_evicted");
    expect(ERROR_KINDS).toEqual([
      "validation_failed",
      "precondition_failed",
      "conflict",
      "timeout",
      "cancelled",
      "busy",
      "unknown_command",
      "unsupported_operation",
      "artifact_missing",
      "invalid_request",
      "internal",
    ]);
  });

  test("validates an honest handshake with enforced limits", () => {
    const message: HandshakeMessage = {
      type: "handshake",
      protocolVersion: PROTOCOL_VERSION,
      host: { name: "Tests", version: "1.0.0", platform: "Unity Test" },
      evaluator: {
        name: "Roslyn.Script",
        languageVersion: "latest",
        persistentState: true,
        supportsCompletion: false,
        cancellation: "cooperative",
      },
      availableEvaluators: ["Roslyn.Script"],
      defaultUsings: ["System"],
      helpers: ["String[] Help()"],
      control: { supported: true, commandsListChanged: false, schemaValidation: false },
      limits: defaultLimits,
      enforces: [
        "maxMessageBytes",
        "maxQueuedCommands",
        "maxResultLength",
        "maxEnumerableElements",
        "maxJobConcurrency",
      ],
    };

    expect(Value.Check(HandshakeMessageSchema, message)).toBe(true);
  });
});
```

- [x] **Step 2: Run the red test**

Run:

```sh
bun test packages/protocol/test/handshake.test.ts
```

Expected: FAIL because no root Bun workspace or `@hotrepl/protocol` source exists yet.

- [x] **Step 3: Add the minimal Bun workspace and protocol exports**

Create the root and package config. Use pinned major versions from the spec's schema-tooling
decision:

```jsonc
// package.json
{
  "name": "hotrepl-workspace",
  "private": true,
  "packageManager": "bun@1.3.14",
  "workspaces": ["packages/*"],
  "scripts": {
    "build": "bun run --filter './packages/*' build",
    "test": "bun test packages/*/test",
    "typecheck": "bun run --filter './packages/*' typecheck",
    "schemas:export": "bun run --cwd packages/protocol schemas:export"
  },
  "devDependencies": {
    "@types/bun": "latest",
    "typescript": "^6.0.3"
  }
}
```

```jsonc
// tsconfig.json
{
  "compilerOptions": {
    "allowImportingTsExtensions": true,
    "exactOptionalPropertyTypes": true,
    "module": "ESNext",
    "moduleResolution": "Bundler",
    "noEmit": true,
    "noUncheckedIndexedAccess": true,
    "strict": true,
    "target": "ES2023",
    "types": ["bun"]
  }
}
```

Use the current Bun/TypeScript 6 configuration shape: Bun documents `types: ["bun"]`, with
`@types/bun` installed, because `@types/bun` is the shim that loads `bun-types`.

```jsonc
// packages/protocol/package.json
{
  "name": "@hotrepl/protocol",
  "version": "0.0.0",
  "private": true,
  "type": "module",
  "exports": { ".": "./src/index.ts" },
  "scripts": {
    "build": "bun test test/**/*.test.ts",
    "typecheck": "tsc -p tsconfig.json",
    "schemas:export": "bun ./scripts/export-schemas.ts"
  },
  "dependencies": {
    "@sinclair/typebox": "^0.34.0"
  }
}
```

```jsonc
// packages/protocol/tsconfig.json
{
  "extends": "../../tsconfig.json",
  "include": ["src/**/*.ts", "test/**/*.ts", "scripts/**/*.ts"]
}
```

Create the protocol source with the constants and `HandshakeMessageSchema` used by the test.
`defaultLimits` must match the approved spec exactly: 4 MiB inbound messages, 100 KiB results, 100
enumerable elements, 32 queued commands, 10 000 ms default eval timeout, one job at a time. The
first implementation also creates `packages/protocol/scripts/export-schemas.ts` so the
`schemas:export` script introduced here is valid from the first commit. The implementation also adds
`node_modules/` to `.gitignore`; `bun install` creates it locally, but dependencies must be
represented only by `bun.lock`.

- [x] **Step 4: Include TS files in dprint**

Modify `dprint.json` so `*.ts` files under `packages/` are formatted by the existing dprint gate. If
the TypeScript plugin is not already configured, add it in the same commit.

- [x] **Step 5: Run green verification**

Run:

```sh
bun install
bun test packages/protocol/test/handshake.test.ts
bun run --cwd packages/protocol typecheck
```

Expected: all pass.

- [x] **Step 6: Commit**

```sh
git add package.json bun.lock tsconfig.json dprint.json packages/protocol docs/superpowers/plans/2026-05-22-hotrepl-clean-architecture-implementation.md
git commit -m "feat(protocol): scaffold TypeScript protocol package"
```

---

## Task 2: C# protocol assembly extraction

**Files:**

- Create: `src/HotRepl.Protocol/HotRepl.Protocol.csproj`
- Create: `src/HotRepl.Protocol/MessageType.cs`
- Create: `src/HotRepl.Protocol/ErrorKind.cs`
- Create: `src/HotRepl.Protocol/Capabilities/*.cs`
- Create: `src/HotRepl.Protocol/Messages/Inbound/*.cs`
- Create: `src/HotRepl.Protocol/Messages/Outbound/*.cs`
- Create: `src/HotRepl.Protocol/Serialization/ProtocolMessageSerializer.cs`
- Modify: `Directory.Packages.props`
- Modify: `tests/HotRepl.Tests/HotRepl.Tests.csproj`
- Test: `tests/HotRepl.Tests/Unit/ProtocolV2MessageSerializerTests.cs`

- [x] **Step 1: Write failing C# protocol tests**

Add `ProtocolV2MessageSerializerTests` covering these cases:

```csharp
[Fact]
public void Handshake_RoundTripsProtocolVersionAndEnforcedLimits()
{
    var message = HandshakeMessage.CreateForTests();
    var json = ProtocolMessageSerializer.Serialize(message);
    var back = ProtocolMessageSerializer.Deserialize<HandshakeMessage>(json);

    Assert.Equal(MessageType.Handshake, back.Type);
    Assert.Equal(2, back.ProtocolVersion);
    Assert.Equal(4 * 1024 * 1024, back.Limits.MaxMessageBytes);
    Assert.Contains("maxJobConcurrency", back.Enforces);
}

[Fact]
public void ParseType_RejectsMissingTypeAsInvalidRequest()
{
    var ex = Assert.Throws<InvalidOperationException>(() =>
        ProtocolMessageSerializer.ParseType("{\"id\":\"missing-type\"}"));

    Assert.Contains("type", ex.Message, StringComparison.Ordinal);
}

[Fact]
public void ErrorEnvelope_UsesClosedKindConstants()
{
    var error = new HotReplErrorEnvelope(
        ErrorKind.ValidationFailed,
        "badArgs",
        "Arguments failed schema validation.",
        retryable: false,
        details: null);

    Assert.Equal("validation_failed", error.Kind);
}
```

- [x] **Step 2: Run red tests**

Run:

```sh
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~ProtocolV2MessageSerializerTests
```

Expected: FAIL because `HotRepl.Protocol` v2 assembly does not exist.

- [x] **Step 3: Implement the protocol assembly**

Create a `netstandard2.1` project with Newtonsoft.Json. Keep it independent: no references to
`HotRepl.Core`, evaluator packages, Unity assemblies, Fleck, or Roslyn. Public message records use
`JsonProperty` names matching the wire format and expose `Type` as a constant default.

- [x] **Step 4: Wire project references**

Reference `src/HotRepl.Protocol/HotRepl.Protocol.csproj` from Core and tests. Do not remove Core's
current v1 protocol files in this task; remove them when the runtime routes exclusively through v2
in Task 3. During execution the test project references `HotRepl.Protocol` through the
`HotReplProtocolV2` alias and Core does not reference it yet. That avoids same-namespace conflicts
with Core's internal v1 protocol types until Task 3 removes them.

- [x] **Step 5: Run green verification**

Run:

```sh
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~ProtocolV2MessageSerializerTests
dotnet build src/HotRepl.Core/ --nologo -v q
```

Expected: all pass.

- [x] **Step 6: Commit**

```sh
git add Directory.Packages.props src/HotRepl.Protocol src/HotRepl.Core/HotRepl.Core.csproj tests/HotRepl.Tests/HotRepl.Tests.csproj tests/HotRepl.Tests/Unit/ProtocolV2MessageSerializerTests.cs docs/superpowers/plans/2026-05-22-hotrepl-clean-architecture-implementation.md
git commit -m "feat(protocol): add C# v2 protocol assembly"
```

---

## Task 3: Runtime v2 foundation cutover

**Files:**

- Modify: `src/HotRepl.Core/ReplConfig.cs`
- Modify: `src/HotRepl.Core/ReplEngine.cs`
- Modify: `src/HotRepl.Core/Server/MessageRouter.cs`
- Modify: `src/HotRepl.Core/Server/ClientRegistry.cs`
- Modify: `src/HotRepl.Core/Protocol/**` (delete v1 records after v2 routes compile)
- Modify: `src/HotRepl.Core/Engine/Commands/**`
- Modify: `src/HotRepl.Core/Control/**`
- Create: `src/HotRepl.Core/Journal/*.cs`
- Test: `tests/HotRepl.Tests/Unit/HandshakeV2Tests.cs`
- Test: `tests/HotRepl.Tests/Unit/MessageRouterV2Tests.cs`
- Test: `tests/HotRepl.Tests/Unit/ClientEvictionTests.cs`
- Test: `tests/HotRepl.Tests/Unit/JournalTests.cs`
- Test: `tests/HotRepl.Tests/Unit/LimitEnforcementTests.cs`
- Test: existing control/job tests updated to v2 semantics

- [x] **Step 1: Write failing runtime tests**

Add tests proving:

```csharp
[Fact]
public void Handshake_AdvertisesProtocolVersionTwoAndNoAuthOrLease()
```

asserts JSON contains `"protocolVersion":2`, `"commandsListChanged":false`,
`"schemaValidation":false`, and no `controlPlane`, `authRequired`, `leaseRequired`, `sessionId`, or
`leaseId`.

```csharp
[Fact]
public void ClientRegistry_SendsSessionEvictedBeforeDisplacingActiveClient()
```

uses fake sockets to assert the first connection receives `session_evicted` before it is closed.

```csharp
[Fact]
public void Journal_QueryReturnsEvalEntriesAfterReset()
```

records an eval entry, resets evaluator state, then queries `kind: "eval"` and still receives the
entry.

```csharp
[Fact]
public void Router_RejectsLegacyControlAuthMessage()
```

routes `{ "type": "control_auth", "id": "legacy" }` and asserts an `invalid_request` error is
addressed to that connection.

```csharp
[Fact]
public void QueueLimit_RejectedRequestReturnsBusyWithoutEnqueueing()
```

sets `MaxQueuedCommands = 1`, queues one command, routes a second command, and asserts a `busy`
response.

- [x] **Step 2: Run red tests**

Run:

```sh
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter "FullyQualifiedName~HandshakeV2Tests|FullyQualifiedName~MessageRouterV2Tests|FullyQualifiedName~ClientEvictionTests|FullyQualifiedName~JournalTests|FullyQualifiedName~LimitEnforcementTests|FullyQualifiedName~ControlRoutingTests|FullyQualifiedName~ControlJobManagerTests"
```

Expected: FAIL because runtime still speaks v1.

- [x] **Step 3: Implement v2 route handling foundation**

Remove auth, lease, `idempotencyKey`, job events, and accepted/cancelling states from the runtime
path. `command_call` returns `command_result` for sync handlers and
`job_accepted { state: "running" }` for job handlers. `job_status_result` returns only `running`,
`done`, `failed`, or `cancelled`. `job_result` is terminal and includes named artifacts.

- [x] **Step 4: Implement journal and limits foundation**

Add two server-owned 1 024-entry journal buffers for eval and command metadata. Record only id,
kind, optional command name, success, durationMs, optional errorKind, and timestamp. Reject
oversized inbound frames before parse using `MaxMessageBytes`. Reject enqueue when queued command or
eval count reaches `MaxQueuedCommands`. Apply output limits to eval/subscription serialization and
command/job output envelopes, then advertise those limits in handshake `enforces[]`.

- [x] **Step 5: Run green verification**

Run the filtered test command from Step 2, then:

```sh
dotnet test tests/HotRepl.Tests/ --nologo -v q
```

Expected: all C# tests pass.

Execution split: this task establishes v2 handshake serialization, observable session eviction,
legacy auth/lease rejection at the router boundary, `MaxMessageBytes`/`MaxQueuedCommands`
enforcement, server-owned journal storage, no runtime lease requirement, and v2 job state strings.
Task 3B below removes the remaining Core-local v1 protocol records and routes all runtime responses
through `HotRepl.Protocol`.

- [x] **Step 6: Commit**

```sh
git add src/HotRepl.Core tests/HotRepl.Tests/Unit docs/superpowers/plans/2026-05-22-hotrepl-clean-architecture-implementation.md
git commit -m "feat(runtime): cut over to protocol v2"
```

---

## Task 3B: Runtime v2 wire-shape cleanup

**Files:**

- Modify: `src/HotRepl.Core/Control/ControlCommandRouter.cs`
- Modify: `src/HotRepl.Core/Protocol/Inbound/CommandCallMessage.cs`
- Modify: `src/HotRepl.Core/Protocol/Inbound/JobCancelMessage.cs`
- Modify: `src/HotRepl.Core/Protocol/Inbound/JobStatusMessage.cs`
- Modify: `src/HotRepl.Core/Protocol/Outbound/CommandResultMessage.cs`
- Modify: `src/HotRepl.Core/Protocol/Outbound/EvalErrorMessage.cs`
- Modify: `src/HotRepl.Core/Protocol/Outbound/JobResultMessage.cs`
- Test: `tests/HotRepl.Tests/Unit/MessageSerializerTests.cs`
- Test: `tests/HotRepl.Tests/Unit/ControlMessageSerializerTests.cs`
- Test: `tests/HotRepl.Tests/Unit/ControlRoutingTests.cs`
- Test: `tests/HotRepl.Tests/Unit/ProtocolV2CleanupTests.cs`

- [x] **Step 1: Write failing cleanup tests**

Assert eval errors use the universal error envelope, sync/job command results expose `output` plus a
named artifact map, and no serialized runtime response contains `diagnostics`, `result`,
`command_accepted`, `leaseId`, or `idempotencyKey`.

- [x] **Step 2: Run red tests**

Run:

```sh
FILTER="FullyQualifiedName~ProtocolV2CleanupTests|\
FullyQualifiedName~MessageSerializerTests|\
FullyQualifiedName~ControlMessageSerializerTests|\
FullyQualifiedName~ControlRoutingTests"
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter "$FILTER"
```

Expected: FAIL while Core-local v1 records are still serialized.

- [x] **Step 3: Strip v1 fields from serialized runtime records**

Keep this commit focused on wire shape: `command_result` and `job_result` serialize `output` plus
named artifact maps, eval errors serialize the universal `error` envelope, and legacy
`leaseId`/`idempotencyKey` fields remain internal compatibility properties only.

The public `HotRepl.Protocol` ownership move is Task 3C. Splitting it keeps the wire-shape fix
reviewable instead of bundling it with the larger command inventory migration.

- [x] **Step 4: Run green verification**

Run the filtered command from Step 2, then:

```sh
dotnet test tests/HotRepl.Tests/ --nologo -v q
dotnet build src/HotRepl.Core/ --nologo -v q
```

Expected: all C# tests and the Core build pass.

- [x] **Step 5: Commit**

```sh
git add src/HotRepl.Core tests/HotRepl.Tests/Unit \
  docs/superpowers/plans/2026-05-22-hotrepl-clean-architecture-implementation.md
git commit -m "feat(runtime): strip v1 fields from responses"
```

---

## Task 3C: Runtime public protocol ownership

**Files:**

- Modify: `src/HotRepl.Core/HotRepl.Core.csproj`
- Modify: `src/HotRepl.Core/ReplEngine.cs`
- Modify: `src/HotRepl.Core/Server/MessageRouter.cs`
- Modify: `src/HotRepl.Core/Control/ControlCommandRouter.cs`
- Modify: `src/HotRepl.Core/Subscriptions/SubscriptionManager.cs`
- Modify: `src/HotRepl.Core/Engine/Commands/**`
- Modify: `src/HotRepl.Core/Evaluator/EvalOutcome.cs`
- Delete or retire: `src/HotRepl.Core/Protocol/**` v1 message records replaced by
  `src/HotRepl.Protocol`
- Modify: `src/HotRepl.Protocol/Serialization/ProtocolMessageSerializer.cs`
- Modify: `src/HotRepl.Protocol/Messages/Outbound/SubscribeResultMessage.cs`
- Test: `tests/HotRepl.Tests/Unit/ProtocolV2CleanupTests.cs`
- Test: `tests/HotRepl.Tests/Unit/ControlRoutingTests.cs`
- Test: `tests/HotRepl.Tests/Unit/MessageRouterV2Tests.cs`
- Modify: `tests/HotRepl.Tests/HotRepl.Tests.csproj`
- Modify: `tests/HotRepl.Tests/Unit/ProtocolV2MessageSerializerTests.cs`
- Delete: stale v1 serializer tests in `tests/HotRepl.Tests/Unit/`

- [x] **Step 1: Write failing public protocol ownership tests**

Assert the runtime handles `commands_list`, `command_describe { name }`, sync commands, job polling,
eval/reset/complete/subscribe, and protocol errors through public `HotRepl.Protocol` records. Remove
legacy serializer tests for `control_auth`, `lease_acquire`, `ping`, and `job_event`.

- [x] **Step 2: Run red tests**

Run:

```sh
FILTER="FullyQualifiedName~ProtocolV2CleanupTests|\
FullyQualifiedName~MessageRouterV2Tests|\
FullyQualifiedName~ControlRoutingTests|\
FullyQualifiedName~ProtocolV2MessageSerializerTests"
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter "$FILTER"
```

Expected: FAIL while Core still relies on duplicate Core-local protocol records.

- [x] **Step 3: Route runtime through `HotRepl.Protocol`**

Remove the external alias from Core's project reference, delete duplicate Core-local records, use
`ProtocolMessageSerializer` for runtime JSON, add `commands_list`, and keep debug-only evaluator
selection as an explicit internal maintenance path outside the public CLI/MCP surface.

- [x] **Step 4: Run green verification**

Run the filtered command from Step 2, then:

```sh
dotnet test tests/HotRepl.Tests/ --nologo -v q
dotnet build src/HotRepl.Core/ --nologo -v q
```

Expected: all C# tests and the Core build pass.

- [x] **Step 5: Commit**

```sh
git add src/HotRepl.Core src/HotRepl.Protocol tests/HotRepl.Tests \
  docs/superpowers/plans/2026-05-22-hotrepl-clean-architecture-implementation.md
git commit -m "feat(runtime): use public protocol v2 records"
```

---
## Task 4: TypeScript testing runtime and SDK core

**Files:**

- Create: `packages/testing/package.json`
- Create: `packages/testing/src/fake-runtime.ts`
- Create: `packages/testing/src/mock-session.ts`
- Create: `packages/testing/src/recorder.ts`
- Create: `packages/testing/src/replay.ts`
- Create: `packages/sdk/package.json`
- Create: `packages/sdk/src/connect.ts`
- Create: `packages/sdk/src/session.ts`
- Create: `packages/sdk/src/errors.ts`
- Create: `packages/sdk/src/artifact.ts`
- Create: `packages/sdk/src/commands.ts`
- Create: `packages/sdk/src/index.ts`
- Test: `packages/sdk/test/session.test.ts`
- Test: `packages/sdk/test/artifact.test.ts`
- Test: `packages/testing/test/fake-runtime.test.ts`

- [x] **Step 1: Write failing SDK tests against FakeRuntime**

Cover connect/handshake validation, protocol mismatch, descriptor caching, sync `run`, job `run`
with polling, `wait:false` job handle, `eval`, `reset`, `complete` unsupported fast-fail, `watch`
async iterable final errors, `journal`, typed `HotReplError`, artifact hash verification, and
session eviction events.

- [x] **Step 2: Run red tests**

Run:

```sh
bun test packages/testing/test packages/sdk/test
```

Expected: FAIL because packages do not exist.

- [x] **Step 3: Implement FakeRuntime and MockSession**

`FakeRuntime` implements every v2 wire message without WebSocket. It stores
commands/descriptors/jobs/artifacts/journal in memory and applies the same limits as the C# runtime.
`MockSession` exposes the SDK `Session` API backed by `FakeRuntime` so consumer facade tests do not
need sockets.

- [x] **Step 4: Implement SDK core**

`connect()` resolves default URL, env URL, explicit URL, and completes handshake before returning.
`Session.run()` consults descriptors, chooses sync/job, polls job status every 250 ms by default,
and returns a `Result`. `Artifact.bytes/json/text/open` rehashes content against `sha256` before
returning it.

- [x] **Step 5: Run green verification**

Run:

```sh
bun test packages/testing/test packages/sdk/test
bun run --cwd packages/testing typecheck
bun run --cwd packages/sdk typecheck
```

Expected: all pass.

- [x] **Step 6: Commit**

```sh
git add packages/testing packages/sdk package.json bun.lock docs/superpowers/plans/2026-05-22-hotrepl-clean-architecture-implementation.md
git commit -m "feat(sdk): add typed session over fake runtime"
```
---

## Task 5: SDK WebSocket adapter and conformance

**Files:**

- Modify: `packages/sdk/src/connect.ts`
- Create: `packages/sdk/src/websocket-transport.ts`
- Create: `packages/conformance/package.json`
- Create: `packages/conformance/src/index.ts`
- Create: `packages/conformance/src/fake-runtime-target.ts`
- Create: `packages/conformance/src/websocket-target.ts`
- Test: `packages/conformance/test/protocol-conformance.test.ts`
- Test: `packages/sdk/test/websocket-transport.test.ts`

- [x] **Step 1: Write failing adapter/conformance tests**

Conformance must assert handshake shape, eval success/error, reset, command list/describe, sync
command result, job accepted/status/result/cancel, journal query, limit rejection, and session
eviction. Run the same suite against `FakeRuntime`; include a WebSocket adapter unit test using an
in-process Bun server.

- [x] **Step 2: Run red tests**

Run:

```sh
bun test packages/sdk/test/websocket-transport.test.ts packages/conformance/test/protocol-conformance.test.ts
```

Expected: FAIL because transport and conformance packages do not exist.

- [x] **Step 3: Implement WebSocket transport**

Use the Web platform `WebSocket` available in Bun. Keep message correlation in the SDK, not
consumers. On `session_evicted`, emit one typed event and cause subsequent calls to throw
`HotReplSessionEvicted`.

- [x] **Step 4: Implement conformance suite**

Export `runConformance(target)` from `@hotrepl/conformance`. The fake-runtime target is the default
CI target. The real C# host target is skipped unless a URL is provided.

- [x] **Step 5: Run green verification**

Run:

```sh
bun test packages/sdk/test packages/conformance/test
bun run --cwd packages/conformance typecheck
```

Expected: all pass.

- [x] **Step 6: Commit**

```sh
git add packages/sdk packages/conformance package.json bun.lock docs/superpowers/plans/2026-05-22-hotrepl-clean-architecture-implementation.md
git commit -m "feat(sdk): add websocket transport and conformance suite"
```

---

## Task 6: CLI package

**Files:**

- Create: `packages/cli/package.json`
- Create: `packages/cli/src/index.ts`
- Create: `packages/cli/src/commands/*.ts`
- Create: `packages/cli/src/format.ts`
- Create: `packages/cli/src/exit-codes.ts`
- Test: `packages/cli/test/cli-output.test.ts`
- Test: `packages/cli/test/exit-codes.test.ts`

- [x] **Step 1: Write failing CLI tests**

Snapshot-test text, JSON, and JSONL output for `info`, `wait`, `doctor`, `eval`, `reset`,
`complete`, `watch`, `run`, `describe`, `artifacts read`, and `journal`. Test exit-code mapping for
every v2 error kind plus `server_unreachable`, `session_evicted`, and `artifact_corrupted`.

- [x] **Step 2: Run red tests**

Run:

```sh
bun test packages/cli/test
```

Expected: FAIL because the CLI package does not exist.

- [x] **Step 3: Implement CLI on SDK only**

Use SDK public API exclusively; do not import protocol transport internals. Keep low-level job
status/cancel and evaluator selection under `hotrepl debug`. Remove auth/profile/lease flags from
the public surface.

- [x] **Step 4: Run green verification**

Run:

```sh
bun test packages/cli/test
bun run --cwd packages/cli typecheck
```

Expected: all pass.

- [x] **Step 5: Commit**

```sh
git add packages/cli package.json bun.lock docs/superpowers/plans/2026-05-22-hotrepl-clean-architecture-implementation.md
git commit -m "feat(cli): add v2 TypeScript command surface"
```

---

## Task 7: MCP server package

**Files:**

- Create: `packages/mcp/package.json`
- Create: `packages/mcp/src/index.ts`
- Create: `packages/mcp/src/tools.ts`
- Create: `packages/mcp/src/session-manager.ts`
- Test: `packages/mcp/test/tools.test.ts`
- Test: `packages/mcp/test/session-eviction.test.ts`

- [x] **Step 1: Write failing MCP tests**

Assert exactly nine tools are registered: `hotrepl_info`, `hotrepl_eval`, `hotrepl_reset`,
`hotrepl_complete`, `hotrepl_list_commands`, `hotrepl_describe_command`, `hotrepl_run`,
`hotrepl_read_artifact`, `hotrepl_journal`. Assert descriptor-derived annotations on `hotrepl_run`
and one persistent SDK session with eviction reported once.

- [x] **Step 2: Run red tests**

Run:

```sh
bun test packages/mcp/test
```

Expected: FAIL because the MCP package does not exist.

- [x] **Step 3: Implement MCP server**

Use stdio transport. Do not register per-game commands as MCP tools. `hotrepl_run` accepts
`{ name, args, timeoutMs? }` and delegates to `Session.run`. On session eviction, report one
notification and reconnect on the next tool call.

- [x] **Step 4: Run green verification**

Run:

```sh
bun test packages/mcp/test
bun run --cwd packages/mcp typecheck
```

Expected: all pass.

- [x] **Step 5: Commit**

```sh
git add packages/mcp package.json bun.lock docs/superpowers/plans/2026-05-22-hotrepl-clean-architecture-implementation.md
git commit -m "feat(mcp): add fixed HotRepl tool server"
```

---

## Task 8: Cleanup Python client and update gates/docs

**Files:**

- Delete: `client/`
- Delete: root Python-only config in `pyproject.toml` after confirming no repo-level non-client
  tooling remains
- Modify: `lefthook.yml`
- Modify: `.github/workflows/*` if Python client jobs exist
- Modify: `AGENTS.md`
- Modify: `README.md`
- Modify: `.claude/skills/hotrepl/SKILL.md`
- Modify: `docs/control-plane-protocol.md` or replace with v2 protocol docs
- Modify: `docs/superpowers/specs/2026-05-22-hotrepl-clean-architecture-design.md` only if final
  implementation intentionally differs from the approved design
- Modify: this plan's checkboxes and deviations section

- [x] **Step 1: Write failing gate/docs checks**

Before deleting files, run:

```sh
uvx ruff check
uvx ruff format --check
uv run --project client --extra dev pyright
```

Expected: these are the old gates that will be removed from `lefthook.yml`. Their old success is not
the v2 acceptance criterion; the acceptance criterion becomes Bun tests/typecheck plus C# tests.

- [x] **Step 2: Delete Python client and remove Python gates**

Delete `client/` and Python-only hook commands (`ruff-check`, `ruff-format-check`, `pyright`,
`pytest`). Keep any repo-level Python config only if another non-client tool still uses it;
otherwise delete it in the same commit.

- [x] **Step 3: Add TS gates**

Add pre-push commands for:

```sh
bun install --frozen-lockfile
bun run test
bun run typecheck
bun run schemas:export
```

Keep dprint, typos, actionlint, dotnet build, and dotnet test.

- [x] **Step 4: Rewrite docs and skill**

Update setup/usage instructions to describe Bun packages, `hotrepl` TypeScript CLI, `hotrepl-mcp`,
v2 protocol, no auth/lease, and loopback/single-client authority.

- [x] **Step 5: Run final HotRepl repo verification**

Run:

```sh
dotnet build src/HotRepl.Core/ --nologo -v q
dotnet test tests/HotRepl.Tests/ --nologo -v q
bun install --frozen-lockfile
bun run test
bun run typecheck
dprint check
typos
actionlint
lefthook run pre-push --force
```

Expected: all pass.

- [x] **Step 6: Commit**

```sh
git add -A
git commit -m "chore: remove v1 Python client surface"
```

---

## Task 9: Consumer migration plan handoff

**Files:**

- Created in `ardenfall-compendium`:
  `docs/superpowers/plans/2026-05-22-ardenfall-hotrepl-v2-migration.md`
- Created in `ancient-kingdoms-mods`: `docs/superpowers/plans/2026-05-22-ak-hotrepl-v2-migration.md`
- Append to: `local://blog-notes-2026-05-22.md`

- [x] **Step 1: Create Ardenfall migration plan**

Plan the exact controller/mod files to touch, generated schema file paths, `CompendiumClient` facade
methods, deletion of `hotrepl-client.ts`, and verification commands from the Ardenfall repo.

- [x] **Step 2: Create Ancient Kingdoms migration plan**

Plan the exact mod/build-tool/docs files to touch, typed command handlers, `build-tool export`
rewrite, deletion of `AutoExporter` and `.exporter-result.json`, exit-code rename, and verification
commands from the AK repo.

- [x] **Step 3: Commit plans in their repos before editing code there**

Use each repo's commit guidelines and worktree workflow. Do not edit consumer code from the HotRepl
worktree.

---

## Deviations from approved spec

- Final review removed the remaining v1 auth/lease configuration and classes rather than only hiding
  them from the v2 wire path. Protocol v2 now uses loopback plus single-client eviction as the
  authority boundary; non-loopback binds warn because there is no auth or lease handshake.
- Runtime handshakes advertise `control.schemaValidation: false` until C# command argument/output
  schema validation is implemented. Generated schemas remain descriptor metadata for SDK, MCP, and
  downstream consumers.
- `maxJobConcurrency` stays in `handshake.enforces[]` because `ControlJobManager` now rejects job
  starts once the configured running-job limit is reached.
- `maxResultLength` and `maxEnumerableElements` stay in `handshake.enforces[]`: eval/subscription
  serialization applies them directly, and command/job outputs now fail with `resultTooLarge` rather
  than returning uncapped payloads.
- The SDK WebSocket transport now rejects `type: "error"` frames at the transport boundary for both
  pending requests and subscription iterators, so callers cannot accidentally resolve universal
  protocol errors as successful responses.
- Protocol-level routing errors are first-class `type: "error"` frames with the universal error
  envelope; the TypeScript SDK rejects the matching request as `HotReplError`.
- TypeScript and .NET packages now use the concrete prerelease version `2.0.0-alpha.0`. Downstream
  plans consume repo-local tarballs/NuGet packages under `vendor/hotrepl/` until registry publishing
  is introduced.
