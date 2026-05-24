# Ardenfall Typed-Command Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate `/Users/joaichberger/Projects/ardenfall-compendium` to HotRepl.Core 3.0.0 typed
commands while preserving the existing export workflow.

**Architecture:** Keep Ardenfall's current command names and controller choreography. Replace C#
`JObject` handlers with typed args/results plus generated schemas, and update the TypeScript raw
WebSocket client to the current HotRepl wire shape (`commands_list`, `majorVersion`, `output`,
artifact maps, terminal `job_result` from `job_status`). Do not adopt `@hotrepl/sdk` in this phase;
dependency/package wiring is not required to validate the typed command authoring API.

**Tech Stack:** C# `netstandard2.1`, HotRepl.Core 3.0.0, Newtonsoft.Json metadata for schemas,
BepInEx/Unity Mono, Bun/TypeScript controller, xUnit mod tests.

**Implementation updates from review:** Ardenfall uses Newtonsoft
`[JsonProperty(..., Required = ...)]` rather than DataAnnotations to avoid a new mod reference;
handlers still validate blank required strings before lookup. The controller/deploy cutover also
keeps HotRepl loopback-only by default (`HOTREPL_BIND_HOST:-127.0.0.1`) because the current protocol
has no auth or lease handshake.

---

## Preconditions

- Work directly in `/Users/joaichberger/Projects/ardenfall-compendium`; the user explicitly
  requested no git worktrees for this roadmap.
- Run `git status --short --branch` before editing. At plan-writing time, unrelated user edits
  existed in item/stat/category extraction files. Do not revert them.
- Build HotRepl first from `/Users/joaichberger/Projects/HotRepl` so
  `src/HotRepl.Core/bin/Debug/netstandard2.1/HotRepl.Core.dll` is current.
- If `mod/libs/HotRepl.Core.dll` is stale, run:

```bash
HOTREPL_CORE_OUT=/Users/joaichberger/Projects/HotRepl/src/HotRepl.Core/bin/Debug/netstandard2.1 \
  bun run mod:copy-libs
```

`mod/libs/` is ignored and is not committed.

## File map

### Ardenfall C# mod

| File                                            | Action                                                            |
| ----------------------------------------------- | ----------------------------------------------------------------- |
| `mod/src/Control/Args/RunBeginArgs.cs`          | Create typed args with `outputBaseDir`, `gameVersion`             |
| `mod/src/Control/Args/RunIdArgs.cs`             | Create required `runId` args                                      |
| `mod/src/Control/Args/EntityPlanArgs.cs`        | Create required `runId`, `entity` args                            |
| `mod/src/Control/Args/EntityExportBatchArgs.cs` | Create required run/entity plus ranged `offset`, `limit`          |
| `mod/src/Control/Results/*.cs`                  | Create one output DTO per command result                          |
| `mod/src/Control/CompendiumCommandResults.cs`   | Keep only diagnostic and artifact helpers                         |
| `mod/src/Control/CompendiumCommandSchemas.cs`   | Delete                                                            |
| `mod/src/Control/CompendiumCommandRegistry.cs`  | Register typed handlers through generic `Register<TArgs,TOutput>` |
| `mod/src/Control/Handlers/*.cs`                 | Convert all handlers to typed interface                           |
| `mod-tests/*CommandTests.cs`                    | Update to typed args/results/artifact dictionary                  |
| `mod-tests/TypedCommandRegistryTests.cs`        | Add router/registry coverage for generated schemas and validation |
| `mod/AGENTS.md`                                 | Update HotRepl dependency note for v3 sidecars if needed          |

### Ardenfall TypeScript controller

| File                                          | Action                                                                            |
| --------------------------------------------- | --------------------------------------------------------------------------------- |
| `controller/src/hotrepl-client.ts`            | Remove auth/lease protocol, parse v3 wire shape                                   |
| `controller/src/export-orchestrator.ts`       | Use `output`, remove auth/lease calls, poll jobs via `job_status` terminal result |
| `controller/src/wait-for-world.ts`            | Use `output`, errors instead of diagnostics arrays                                |
| `controller/src/cli.ts`                       | Remove token plumbing if now unused                                               |
| `controller/test/hotrepl-client.test.ts`      | Update fake server to v3 wire                                                     |
| `controller/test/export-orchestrator.test.ts` | Update fake client/output expectations                                            |
| `controller/test/wait-for-world.test.ts`      | Update fake client/output expectations                                            |
| `package.json`                                | Remove `HOTREPL_TOKEN` script plumbing if no code consumes it                     |

### HotRepl docs

| File                                                                              | Action                                             |
| --------------------------------------------------------------------------------- | -------------------------------------------------- |
| `docs/superpowers/specs/2026-05-24-typed-commands-roadmap.md`                     | Mark Phase 2 spec link/status when complete        |
| `docs/superpowers/specs/2026-05-24-typed-commands-phase-2-ardenfall-migration.md` | Update if implementation changes a design decision |

---

## Task 1: C# typed DTOs and helper cutover

**Files:**

- Create: `mod/src/Control/Args/RunBeginArgs.cs`
- Create: `mod/src/Control/Args/RunIdArgs.cs`
- Create: `mod/src/Control/Args/EntityPlanArgs.cs`
- Create: `mod/src/Control/Args/EntityExportBatchArgs.cs`
- Create: `mod/src/Control/Results/CompendiumInfoResult.cs`
- Create: `mod/src/Control/Results/CompendiumPreflightResult.cs`
- Create: `mod/src/Control/Results/ContinueFromMenuResult.cs`
- Create: `mod/src/Control/Results/RunBeginResult.cs`
- Create: `mod/src/Control/Results/RunStatusResult.cs`
- Create: `mod/src/Control/Results/EntityPlanResult.cs`
- Create: `mod/src/Control/Results/EntityExportBatchResult.cs`
- Create: `mod/src/Control/Results/RunFinalizeResult.cs`
- Create: `mod/src/Control/Results/RunDiscardResult.cs`
- Create: `mod/src/Control/Results/GameQuitResult.cs`
- Modify: `mod/src/Control/CompendiumCommandResults.cs`

- [ ] **Step 1: Write the failing compile/test expectation**

Update one existing command test (`mod-tests/EntityPlanCommandTests.cs`) to use typed args and typed
output:

```csharp
var result = await command.ExecuteAsync(
    null!,
    new EntityPlanArgs { RunId = run.RunId, Entity = "item" },
    CancellationToken.None);

Assert.True(result.Succeeded);
Assert.Empty(result.Diagnostics);
Assert.Equal(0, result.Output!.Total);
```

Run:

```bash
dotnet test mod-tests/ArdenfallCompendium.Tests.csproj --nologo -v q --filter FullyQualifiedName~EntityPlanCommandTests
```

Expected: FAIL because `EntityPlanArgs` does not exist and the handler still expects `JObject`.

- [ ] **Step 2: Add args DTOs**

Create `mod/src/Control/Args/RunBeginArgs.cs`:

```csharp
using Newtonsoft.Json;

namespace ArdenfallCompendium.Control.Args;

public sealed class RunBeginArgs
{
    [JsonProperty("outputBaseDir")]
    public string? OutputBaseDir { get; set; }

    [JsonProperty("gameVersion")]
    public string? GameVersion { get; set; }
}
```

Create `mod/src/Control/Args/RunIdArgs.cs`:

```csharp
using Newtonsoft.Json;

namespace ArdenfallCompendium.Control.Args;

public sealed class RunIdArgs
{
    [JsonProperty("runId", Required = Required.Always)]
    public string RunId { get; set; } = string.Empty;
}
```

Create `mod/src/Control/Args/EntityPlanArgs.cs`:

```csharp
using Newtonsoft.Json;

namespace ArdenfallCompendium.Control.Args;

public sealed class EntityPlanArgs
{
    [JsonProperty("runId", Required = Required.Always)]
    public string RunId { get; set; } = string.Empty;

    [JsonProperty("entity", Required = Required.Always)]
    public string Entity { get; set; } = string.Empty;
}
```

Create `mod/src/Control/Args/EntityExportBatchArgs.cs`:

```csharp
using Newtonsoft.Json;

namespace ArdenfallCompendium.Control.Args;

public sealed class EntityExportBatchArgs
{
    [JsonProperty("runId", Required = Required.Always)]
    public string RunId { get; set; } = string.Empty;

    [JsonProperty("entity", Required = Required.Always)]
    public string Entity { get; set; } = string.Empty;

    [JsonProperty("offset", Required = Required.Always)]
    public int Offset { get; set; }

    [JsonProperty("limit", Required = Required.Always)]
    public int Limit { get; set; }
}
```

- [ ] **Step 3: Add result DTOs**

Create one public type per file under `mod/src/Control/Results/`. Every property must carry
`[JsonProperty("lowerCamelName", Required = ...)]` so generated output schemas advertise required
fields; nullable output properties use `Required.AllowNull`.

Required result shapes:

```csharp
public sealed class CompendiumInfoResult
{
    [JsonProperty("apiVersion")] public int ApiVersion { get; set; }
    [JsonProperty("extractorVersion")] public string ExtractorVersion { get; set; } = string.Empty;
    [JsonProperty("gameVersion")] public string GameVersion { get; set; } = string.Empty;
    [JsonProperty("supportedEntities")] public string[] SupportedEntities { get; set; } = Array.Empty<string>();
}
```

```csharp
public sealed class EntityPlanResult
{
    [JsonProperty("entity")] public string Entity { get; set; } = string.Empty;
    [JsonProperty("total")] public int Total { get; set; }
    [JsonProperty("batchSize")] public int BatchSize { get; set; }
    [JsonProperty("batches")] public int Batches { get; set; }
}
```

Follow the same pattern for:

- `CompendiumPreflightResult`: `ready`, `passed`, `completedAt`, `checks`.
- `ContinueFromMenuResult`: `clicked`, `button`.
- `RunBeginResult`: `runId`, `workspaceDir`.
- `RunStatusResult`: `runId`, `state`, `counts`, `finalized`, `workspaceDir`, `publishedDir`.
- `EntityExportBatchResult`: `entity`, `offset`, `limit`, `written`, `total`.
- `RunFinalizeResult`: `runId`, `publishedDir`, `manifestPath`.
- `RunDiscardResult`: `runId`, `discarded`.
- `GameQuitResult`: `quitting`.

Use existing DTO types where useful (`PreflightCheck`) rather than duplicating them.

- [ ] **Step 4: Replace result helpers**

`mod/src/Control/CompendiumCommandResults.cs` should no longer mention old non-generic
`ControlCommandResult` or `ControlCommandError`. Keep:

```csharp
public static ControlCommandResult<TOutput> Validation<TOutput>(string code, string message, object? details = null) =>
    ControlCommandResult.ValidationFailed<TOutput>(code, message, details);

public static ControlCommandResult<TOutput> Precondition<TOutput>(string code, string message, object? details = null) =>
    ControlCommandResult.PreconditionFailed<TOutput>(code, message, details);
```

`FileArtifact(...)` remains unchanged.

- [ ] **Step 5: Run focused compile**

Run:

```bash
dotnet build mod/ArdenfallCompendium.csproj -c Debug --nologo -v q
```

Expected: still FAIL until handlers are migrated in Task 2, but failures should point at old handler
interfaces/usages rather than missing DTO/helper types.

---

## Task 2: C# handler and registry migration

**Files:**

- Modify: `mod/src/Control/CompendiumCommandRegistry.cs`
- Modify: every file under `mod/src/Control/Handlers/`
- Delete: `mod/src/Control/CompendiumCommandSchemas.cs`
- Modify: `mod-tests/EntityPlanCommandTests.cs`
- Modify: `mod-tests/EntityExportBatchCommandTests.cs`
- Modify: `mod-tests/RunFinalizeCommandTests.cs`
- Create: `mod-tests/TypedCommandRegistryTests.cs`

- [ ] **Step 1: Update registry helper**

Change the private helper to generic registration:

```csharp
private void Register<TArgs, TOutput>(IControlCommandHandler<TArgs, TOutput> handler)
{
    _registrations.Add(GlobalControlCommandRegistry.Instance.Register(handler));
}
```

No command names change.

- [ ] **Step 2: Migrate no-arg handlers**

Convert `CompendiumInfoCommand`, `CompendiumPreflightCommand`, `ContinueFromMenuCommand`, and
`GameQuitCommand` to implement typed `IControlCommandHandler<EmptyArgs, TResult>`.

Pattern:

```csharp
public sealed class GameQuitCommand : IControlCommandHandler<EmptyArgs, GameQuitResult>
{
    public string Name => "game.quit";
    public int Version => 1;
    public ControlCommandKind Kind => ControlCommandKind.Synchronous;
    public bool MutatesState => true;

    public ValueTask<ControlCommandResult<GameQuitResult>> ExecuteAsync(
        ControlCommandContext context,
        EmptyArgs args,
        CancellationToken cancellationToken)
    {
        Application.Quit();
        return new(ControlCommandResult.Ok(new GameQuitResult { Quitting = true }));
    }
}
```

- [ ] **Step 3: Migrate run handlers**

Convert `RunBeginCommand`, `RunStatusCommand`, `RunDiscardCommand`, and `RunFinalizeCommand`.

Use `RunBeginArgs` and `RunIdArgs`. Domain errors use typed helper calls:

```csharp
return new(
    CompendiumCommandResults.Validation<RunStatusResult>(
        "unknownRun",
        $"Unknown run '{args.RunId}'."));
```

In `RunFinalizeCommand`, build artifact dictionaries with `StringComparer.Ordinal`:

```csharp
var artifacts = new Dictionary<string, ArtifactRef>(StringComparer.Ordinal)
{
    ["manifest"] = CompendiumCommandResults.FileArtifact("manifest", manifestPath, "application/json", manifestHash),
};
```

Return `ControlCommandResult.Ok(result, artifacts)`.

- [ ] **Step 4: Migrate entity handlers**

Convert `EntityPlanCommand` and `EntityExportBatchCommand`.

`EntityExportBatchCommand.Validate(...)` should accept `EntityExportBatchArgs args` and return
`ControlCommandResult<EntityExportBatchResult>?`. It should read `args.Offset`, `args.Limit`,
`args.RunId`, and `args.Entity`, not `JObject`.

- [ ] **Step 5: Add registry and typed-validation tests**

Create `mod-tests/TypedCommandRegistryTests.cs` with tests that:

- assert generated schemas use lower-camel names and required output fields;
- assert `entity.exportBatch` is advertised as a mutating job with required `runId`, `entity`,
  `offset`, and `limit` args;
- call `EntityExportBatchCommand` directly with blank `runId` and blank `entity` to verify typed
  validation diagnostics (`runIdRequired`, `entityRequired`) are returned before run lookup or
  entity planning logic.

`ControlCommandRouter` is internal to HotRepl.Core in the consumed DLL, so Ardenfall's downstream
coverage stays at the public registry/handler boundary. HotRepl.Core owns router-level schema
validation tests.

- [ ] **Step 6: Run C# tests**

Run:

```bash
dotnet test mod-tests/ArdenfallCompendium.Tests.csproj --nologo -v q --filter "FullyQualifiedName~EntityPlanCommandTests|FullyQualifiedName~EntityExportBatchCommandTests|FullyQualifiedName~RunFinalizeCommandTests|FullyQualifiedName~TypedCommandRegistryTests"
```

Expected: PASS.

---

## Task 3: TypeScript controller protocol migration

**Files:**

- Modify: `controller/src/hotrepl-client.ts`
- Modify: `controller/src/export-orchestrator.ts`
- Modify: `controller/src/wait-for-world.ts`
- Modify: `controller/src/cli.ts`
- Modify: `controller/test/hotrepl-client.test.ts`
- Modify: `controller/test/export-orchestrator.test.ts`
- Modify: `controller/test/wait-for-world.test.ts`
- Modify: `package.json`

- [ ] **Step 1: Update failing client tests first**

In `controller/test/hotrepl-client.test.ts`, change the fake server to send a HotRepl handshake:

```ts
ws.send(JSON.stringify({ type: "handshake", protocolVersion: 2 }));
```

Replace `command_describe` handling with `commands_list` returning:

```ts
{
  type: "commands_list_result",
  id,
  commands: [{ name: "compendium.info", majorVersion: 1, kind: "sync", mutatesState: false }],
}
```

Change command results to use:

```ts
{ type: "command_result", id, status: "ok", output: { ok: true }, artifacts: {}, durationMs: 0 }
```

Change job polling so `job_status` returns terminal `job_result` for completed jobs. Remove the
client call to `jobResult()` from tests.

Run:

```bash
bun test controller/test/hotrepl-client.test.ts
```

Expected: FAIL because production client still sends auth/lease and expects old fields.

- [ ] **Step 2: Update `HotReplClient`**

Remove `authenticate`, `acquireLease`, `sessionId`, `leaseId`, `idempotencyKey`, and manual
`job_result` requests. `connect()` should resolve only after it receives the `handshake` message.
`describeCommands()` sends `{ type: "commands_list", id }` and parses `majorVersion` into the
controller descriptor's `version` field.

`parseCommandResult` becomes:

```ts
function parseCommandResult(data: JsonObject): CommandResult {
  if (data.status === "failed") {
    throw new ControlCommandError(parseControlError(isObject(data.error) ? data.error : undefined));
  }
  return {
    status: String(data.status),
    output: isObject(data.output) ? data.output : {},
    artifacts: isObject(data.artifacts) ? parseArtifacts(data.artifacts) : {},
  };
}
```

`jobStatus()` must return either `JobStatus` or a terminal `CommandResult` shape internally; expose
a helper that lets `export-orchestrator` wait until terminal result without sending `job_result`.

- [ ] **Step 3: Update orchestrator and wait-for-world**

Remove auth/lease logic. Replace every `.result` read with `.output`:

```ts
const preflight = await options.client.call("compendium.preflight", {});
if (preflight.output.ready !== true) throw new Error(formatPreflightFailure(preflight.output));
```

For export batch jobs, use a single client method that starts the job and waits for terminal output,
or keep `startJob` + `waitForJob` if `waitForJob` returns the terminal command result from
`job_status`.

- [ ] **Step 4: Remove token plumbing**

Remove `token` from `ExportOptions`, CLI parsing, and `package.json`'s `hotrepl:export` script.

- [ ] **Step 5: Run controller tests**

Run:

```bash
bun test controller/test/hotrepl-client.test.ts controller/test/export-orchestrator.test.ts controller/test/wait-for-world.test.ts
```

Expected: PASS.

---

## Task 4: Full verification and docs closeout

**Files:**

- Modify: `mod/AGENTS.md`
- Modify: `docs/superpowers/roadmap.md`
- Modify in HotRepl repo:
  `docs/superpowers/specs/2026-05-24-typed-commands-phase-2-ardenfall-migration.md`
- Modify in HotRepl repo: `docs/superpowers/specs/2026-05-24-typed-commands-roadmap.md`

- [ ] **Step 1: Run full Ardenfall gates**

Run from `/Users/joaichberger/Projects/ardenfall-compendium`:

```bash
bun test controller/test
dotnet test mod-tests/ArdenfallCompendium.Tests.csproj --nologo -v q
dotnet build mod/ArdenfallCompendium.csproj -c Debug --nologo -v q
bun run typecheck
```

Expected: all pass with zero C# warnings.

- [ ] **Step 2: Run setup when environment is available**

If `.env` contains `HOTREPL_REPO`, `ARDENFALL_MANAGED_DIR`, `HOTREPL_CORE_OUT`,
`HOTREPL_BEPINEX_OUT`, and `ARDENFALL_PLUGINS_DIR`, run:

```bash
bun run hotrepl:setup
```

Expected: HotRepl host, UnityCommands sidecar, and Ardenfall mod deploy to the game plugin folder.
If environment is missing, record that local automated gates passed and live setup was not
exercised.

- [ ] **Step 3: Update docs**

Update Ardenfall `mod/AGENTS.md` to say the mod implements HotRepl v3 typed handlers and that
runtime deployment must include HotRepl's sidecar DLLs from the BepInEx host output.

Update Ardenfall `docs/superpowers/roadmap.md` with a short operational slice noting the HotRepl v3
migration, local verification evidence, and any live setup/export evidence.

Update the HotRepl Phase 2 spec if implementation required a different design decision.

- [ ] **Step 4: Commit Ardenfall changes**

Use a useful multi-paragraph commit body:

```bash
git add mod/src/Control controller/src controller/test mod-tests mod/AGENTS.md docs/superpowers/roadmap.md package.json bun.lock
git commit -F /tmp/ardenfall-hotrepl-v3-commit.txt
```

Suggested subject:

```text
feat(mod): migrate HotRepl commands to typed v3 API
```

Body should explain that generated schemas now come from typed DTOs and the controller now consumes
HotRepl's current output/artifact-map wire shape.

- [ ] **Step 5: Commit HotRepl docs closeout**

If HotRepl docs changed after implementation:

```bash
git add docs/superpowers/specs/2026-05-24-typed-commands-phase-2-ardenfall-migration.md docs/superpowers/specs/2026-05-24-typed-commands-roadmap.md docs/superpowers/plans/2026-05-24-typed-commands-phase-2-ardenfall-migration.md
git commit -F /tmp/hotrepl-phase2-closeout-commit.txt
```

Suggested subject:

```text
docs(spec): record Ardenfall typed-command migration
```

---

## Self-review checklist

- The plan covers every Phase 2 spec acceptance criterion.
- The command inventory preserves all ten names and versions.
- The plan explicitly removes old auth/lease/job-result request protocol.
- The plan avoids touching unrelated item/stat/category user edits in the Ardenfall checkout.
- Verification commands cover C#, controller tests, and TypeScript typecheck.

## Closeout evidence

Implementation followed the reviewed adjustments:

- Ardenfall uses Newtonsoft `Required` metadata rather than DataAnnotations and keeps blank-string
  validation in handlers.
- Controller tests were moved first to the current v3 wire shape (`handshake`, `commands_list`,
  `job_accepted`, terminal `job_result` from `job_status`, `output`, artifact maps).
- `hotrepl:deploy` now defaults `HOTREPL_BIND_HOST` to `127.0.0.1`; local setup can still override
  it explicitly.

Observed verification from `/Users/joaichberger/Projects/ardenfall-compendium`:

```bash
bun test controller/test
dotnet test mod-tests/ArdenfallCompendium.Tests.csproj --nologo -v q
dotnet build mod/ArdenfallCompendium.csproj -c Debug --nologo -v q
bun run typecheck
bun run hotrepl:setup
bun run hotrepl:export
```

Live export published `snapshots/snapshots/0.0.10.91-20260524-1022238608580` with counts
`{ item: 1273, stat-type: 20, item-category: 7, item-tag: 28 }`, diagnostics
`{ fatal: 0, diagnostic: 1807 }`, `pipeline/dist/data.sqlite` at 6,238,208 bytes, 1,779 asset refs,
and `game.quit` completed.
