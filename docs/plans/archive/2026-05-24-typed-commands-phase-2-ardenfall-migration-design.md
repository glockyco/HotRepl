---
title: "Phase 2 — Ardenfall typed-command migration"
type: spec
status: implemented
created: 2026-05-24
parent:
superseded_by:
archived: 2026-06-25
---

# Phase 2 — Ardenfall typed-command migration

Detailed design for the second phase of the typed-commands roadmap
([`2026-05-24-typed-commands-roadmap.md`](2026-05-24-typed-commands-roadmap.md)).

Phase 2 migrates `/Users/joaichberger/Projects/ardenfall-compendium` from the pre-v3 HotRepl command
surface to the Phase 1 typed command API. It does not add new Ardenfall extraction scope. The
snapshot contents, controller phase ordering, deployment scripts, and live workflow stay
semantically unchanged.

---

## 1. Goal

Make Ardenfall the first real consumer of `HotRepl.Core` 3.0.0's typed command API. Every mod
command currently implementing the old non-generic `IControlCommandHandler` must become an
`IControlCommandHandler<TArgs, TOutput>` with typed request and response DTOs, generated schemas,
server-side argument validation, top-level artifact maps, and structured diagnostics.

The migration is a clean cutover: no compatibility shim, no duplicate command names, no retained
`JObject` request parsing in handlers, and no old `result` / artifact-array controller assumptions.

## 2. Scope

### In scope

- Update Ardenfall's copied HotRepl compile/runtime DLLs to the current HotRepl 3.0.0 output.
- Migrate these ten commands without renaming them:
  - `compendium.info`
  - `compendium.preflight`
  - `compendium.continueFromMenu`
  - `run.begin`
  - `run.status`
  - `entity.plan`
  - `entity.exportBatch`
  - `run.finalize`
  - `run.discard`
  - `game.quit`
- Replace manual `JObject` args parsing with typed DTOs annotated with `Newtonsoft.Json`
  `[JsonProperty]` metadata.
- Preserve existing lower-camel wire names through `[JsonProperty]` attributes so the controller
  keeps sending `{ "runId": ... }`, not `{ "RunId": ... }`.
- Preserve the controller choreography: preflight or wait-for-world, begin, plan, per-batch job,
  finalize, validate snapshot, run pipeline, quit.
- Update the raw TypeScript WebSocket client to the current HotRepl v2 wire contract:
  `commands_list`, `majorVersion`, `inputSchema`, `output`, artifact maps, `job_status` returning
  terminal `job_result`, and protocol-level `error` envelopes.
- Update Ardenfall tests and docs affected by the command API.

### Out of scope

- Adding new entity types, item fields, snapshots, or site features.
- Replacing the controller with `@hotrepl/sdk`. That is useful later, but Phase 2's validation
  target is the C# typed command authoring API and the existing export workflow. Introducing local
  package wiring here would add dependency churn unrelated to the migration.
- Live game smoke unless local environment variables and game state are already available. The phase
  must keep `bun run hotrepl:setup` runnable; local automated gates are required.
- Changing HotRepl.Core again unless Ardenfall exposes a real API gap.

## 3. Design

### 3.1 C# command shape

Handlers move from:

```csharp
ValueTask<ControlCommandResult> ExecuteAsync(ControlCommandContext context, JObject args, CancellationToken token)
```

to:

```csharp
ValueTask<ControlCommandResult<TOutput>> ExecuteAsync(
    ControlCommandContext context,
    TArgs args,
    CancellationToken token);
```

Each command exposes `Name`, `Version`, `Kind`, and `MutatesState` directly. Schemas come from the
registered `TArgs` and `TOutput` types via HotRepl's `SchemaCache`, so `CompendiumCommandSchemas` is
removed.

Args classes live under `mod/src/Control/Args/`; result classes live under
`mod/src/Control/Results/`. Every public wire property is explicitly annotated:

```csharp
public sealed class RunIdArgs
{
    [JsonProperty("runId", Required = Required.Always)]
    public string RunId { get; set; } = string.Empty;
}
```

Required property presence is expressed through Newtonsoft metadata and handled by HotRepl before
the handler runs. Empty-string/domain validation remains in handlers (`runIdRequired`,
`entityRequired`, `unknownRun`, unsupported entity values, finalized runs, missing plans, incomplete
chunks). This keeps the mod free of a new DataAnnotations runtime/reference requirement.

### 3.2 Diagnostics and results

`CompendiumCommandResults` stops creating v2 result envelopes. It remains only as an Ardenfall-local
helper for:

- `Validation<TOutput>(code, message, details)`
- `Precondition<TOutput>(code, message, details)`
- `FileArtifact(logicalName, path, contentType, sha256)`
- optional `Ok<TOutput>` pass-throughs when they make call sites clearer

Failures return `ControlCommandResult.ValidationFailed<TOutput>` or
`ControlCommandResult.PreconditionFailed<TOutput>`. They do not throw for expected invalid input.
Unexpected exceptions still fail the job/command through HotRepl's internal error path.

Artifacts use v3's named top-level map. Batch export returns `{ "item.chunk.000000": artifact }`.
Finalize returns keys such as `manifest`, `items`, `asset-manifest`, `master-tooltip`, `stat-types`,
`item-categories`, `item-tags`, and optionally `diagnostics`. The output DTO keeps paths and counts;
artifact bytes remain out-of-band.

### 3.3 Controller wire contract

The existing controller can stay raw-WebSocket for this phase, but it must speak the current HotRepl
wire contract:

- connection waits for the `handshake` notification and rejects mismatched protocol versions;
- no `control_auth`, `lease_acquire`, `sessionId`, `leaseId`, token, or idempotency key messages;
- command discovery uses `commands_list` and checks `majorVersion`;
- command responses read `output`, not `result`;
- artifacts are a name-keyed object, not an array;
- failed commands are `command_result` / `job_result` with `status = "failed"` and `error`, or a
  protocol-level `error` message;
- job clients poll `job_status`; a terminal poll may return `job_result`; no client sends a
  `job_result` request.

The controller-facing `CommandResult<T>` type therefore becomes:

```ts
interface CommandResult<TOutput = Record<string, unknown>> {
  status: "ok" | "failed" | string;
  output: TOutput;
  artifacts: Record<string, ArtifactRef>;
}
```

Expected command/domain failures throw `ControlCommandError` built from the returned `error`
envelope. Orchestration continues to treat them as failures. The deploy helper now defaults
HotRepl's bind host to `127.0.0.1`. Operators can still opt into a broader bind host through
`HOTREPL_BIND_HOST`, but Phase 2 does not reintroduce protocol-level auth or leases.

### 3.4 Command inventory

| Command                       | Args type               | Output type                 | Kind | Mutates |
| ----------------------------- | ----------------------- | --------------------------- | ---- | ------- |
| `compendium.info`             | `EmptyArgs`             | `CompendiumInfoResult`      | sync | no      |
| `compendium.preflight`        | `EmptyArgs`             | `CompendiumPreflightResult` | sync | no      |
| `compendium.continueFromMenu` | `EmptyArgs`             | `ContinueFromMenuResult`    | sync | yes     |
| `run.begin`                   | `RunBeginArgs`          | `RunBeginResult`            | sync | yes     |
| `run.status`                  | `RunIdArgs`             | `RunStatusResult`           | sync | no      |
| `entity.plan`                 | `EntityPlanArgs`        | `EntityPlanResult`          | sync | no      |
| `entity.exportBatch`          | `EntityExportBatchArgs` | `EntityExportBatchResult`   | job  | yes     |
| `run.finalize`                | `RunIdArgs`             | `RunFinalizeResult`         | sync | yes     |
| `run.discard`                 | `RunIdArgs`             | `RunDiscardResult`          | sync | yes     |
| `game.quit`                   | `EmptyArgs`             | `GameQuitResult`            | sync | yes     |

### 3.5 Test strategy

C# tests cover command behavior directly against typed handlers:

- typed args are passed directly for happy-path unit coverage;
- invalid domain values return failed `ControlCommandResult<T>` diagnostics;
- registry tests assert generated schema casing, required input fields, required output fields, and
  job/mutation metadata;
- direct handler tests assert blank required strings fail with typed validation diagnostics;
- artifact-producing commands assert named artifact dictionaries instead of arrays.

TypeScript tests cover the controller protocol cutover with a fake WebSocket server:

- handshake is observed;
- required commands are discovered via `commands_list`;
- no auth/lease messages are sent;
- output/artifact maps parse correctly;
- job polling consumes terminal `job_result` from `job_status`;
- failed command results and protocol errors reject with typed `ControlCommandError`.

Full gates for the phase:

```bash
bun test controller/test
bun run typecheck
dotnet test mod-tests/ArdenfallCompendium.Tests.csproj --nologo -v q
dotnet build mod/ArdenfallCompendium.csproj -c Debug --nologo -v q
```

Run `bun run hotrepl:setup` when the local `.env` points at the Ardenfall install and HotRepl repo.

## 4. Acceptance criteria

- Ardenfall builds against `HotRepl.Core` 3.0.0 with no references to the deleted non-generic
  `IControlCommandHandler` or old `ControlCommandResult` shape.
- All ten command names and versions remain unchanged.
- `commands_list` / `command_describe` expose generated input/output schemas for typed args/results.
- `entity.exportBatch` and `run.finalize` still write the same snapshot files and artifact refs.
- The controller no longer sends auth/lease/job-result request messages and consumes `output` plus
  artifact maps.
- Existing controller export tests and mod command tests pass after migration.
- HotRepl roadmap/spec files and Ardenfall docs/roadmap identify the phase as migrated.

## 5. Implementation closeout

Ardenfall migrated with the clean cutover described above. The final implementation uses Newtonsoft
`Required` metadata for generated required fields and explicit handler checks for blank `runId` /
`entity` values. The controller stays raw WebSocket but now consumes `handshake`, `commands_list`,
`job_accepted`, terminal `job_result` from `job_status`, `output`, and artifact maps.

Verification completed in `/Users/joaichberger/Projects/ardenfall-compendium`:

```bash
bun test controller/test
dotnet test mod-tests/ArdenfallCompendium.Tests.csproj --nologo -v q
dotnet build mod/ArdenfallCompendium.csproj -c Debug --nologo -v q
bun run typecheck
bun run hotrepl:setup
bun run hotrepl:export
```

The setup run used the local `.env`'s explicit `HOTREPL_BIND_HOST=0.0.0.0`; the committed script
default remains loopback-only. The live export published
`snapshots/snapshots/0.0.10.91-20260524-1022238608580` from Ardenfall Demo `0.0.10.91`, with counts
`{ item: 1273, stat-type: 20, item-category: 7, item-tag: 28 }`, diagnostics
`{ fatal: 0, diagnostic: 1807 }`, `pipeline/dist/data.sqlite` at 6,238,208 bytes, 1,779 asset refs,
and a completed `game.quit`.

## 6. Risks and decisions

- **Lower-camel wire names:** use `[JsonProperty]` everywhere. This avoids a controller-breaking
  PascalCase switch and keeps schema output stable for agents.
- **In-memory artifact writer:** Phase 1's `InMemoryArtifactWriter` is adequate for Ardenfall's test
  path because existing handlers write real files first and publish artifact refs. If live finalize
  exposes memory pressure, that becomes a HotRepl host storage follow-up, not an Ardenfall command
  API blocker.
- **Partial-failure output:** Ardenfall currently treats validation/precondition failures as
  no-output failures. Phase 2 keeps that rule; successful snapshots with row diagnostics remain
  successful results and write `diagnostics.json` as an artifact.
- **User edits in Ardenfall:** the checkout currently has unrelated item/stat/category changes. The
  migration must avoid reverting them and keep edits focused to control/controller/docs files unless
  a test update must adapt around the changed code.
- **Loopback default:** because protocol v2/v3 has no auth or lease handshake, Ardenfall deployment
  defaults to `127.0.0.1`. Operators who need host-reachable automation must opt in with
  `HOTREPL_BIND_HOST` and provide a trusted external network boundary.
