# Phase 3 — Ancient Kingdoms typed-command migration

Detailed design for the third phase of the typed-commands roadmap
([`2026-05-24-typed-commands-roadmap.md`](2026-05-24-typed-commands-roadmap.md)).

Phase 3 migrates `/Users/joaichberger/Projects/ancient-kingdoms-mods` from launch-flag export
automation (`AutoExporter` + `.exporter-result.json`) to `HotRepl.Core` 3.0.0 typed commands. The
migration validates the typed-command API against an IL2CPP MelonLoader game, multi-frame Unity
workflows, job progress, cancellation, and large named artifact maps.

---

## 1. Goal

Make Ancient Kingdoms the second real consumer of HotRepl's typed command API and the first IL2CPP
consumer. The automated export flow must be driven by HotRepl WebSocket commands rather than Steam
launch flags or a file-polling completion side channel.

The migration is a clean cutover:

- no `AutoExporter` launch-flag path;
- no `.exporter-result.json` producer or consumer;
- no legacy HotRepl profile/auth/lease vocabulary;
- no duplicate automated export surface;
- no `System.Text.Json` dependency added for this flow;
- command names and versions remain stable once introduced.

## 2. Research inputs

The design is grounded in current primary-source guidance:

- OWASP's WebSocket Security Cheat Sheet says WebSocket messages have no built-in authentication or
  authorization, must be treated as untrusted input, should be schema-validated, and should be
  bounded by message-size/rate/resource controls. Phase 3 keeps HotRepl's loopback-only default and
  does not add a fake auth/lease protocol. Operators who bind wider must provide an external trusted
  network boundary.
  <https://cheatsheetseries.owasp.org/cheatsheets/WebSocket_Security_Cheat_Sheet.html>
- Microsoft's asynchronous request-reply pattern validates the accepted-job + status-poll + terminal
  result shape for long-running work. Phase 3 uses `command_call` -> `job_accepted` -> repeated
  `job_status` -> terminal `job_result` for the export job.
  <https://learn.microsoft.com/en-us/azure/architecture/patterns/asynchronous-request-reply>
- JSON Schema requires properties only when they are explicitly listed in `required`. Phase 3 uses
  Newtonsoft `[JsonProperty(..., Required = ...)]` metadata on DTOs and tests generated schemas for
  lower-camel names and required fields.
  <https://json-schema.org/understanding-json-schema/reference/object>
- Unity documents that most Unity APIs are main-thread-only and that `Task` continuations called
  from the Unity main thread resume through `UnitySynchronizationContext` on the next Update tick.
  Phase 3 uses `async`/`await` and `Task.Yield()` only from HotRepl's main-thread command execution
  path. <https://docs.unity3d.com/6000.3/Documentation/Manual/async-awaitable-continuations.html>
- Microsoft documents that `ClientWebSocket` supports exactly one send and one receive concurrently.
  Phase 3's build-tool runner serializes sends and receives rather than issuing parallel request
  awaits against one socket.
  <https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets.clientwebsocket.receiveasync>
  <https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets.clientwebsocket.sendasync>
- Microsoft documents `ValueTask<T>` single-consumption constraints. Phase 3 uses `ValueTask`
  because it is HotRepl's handler contract, returns synchronously completed values directly where
  possible, and never stores or re-awaits handler `ValueTask`s.
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1>
- MelonLoader's `OnLateInitializeMelon` runs after `OnInitializeMelon` and after Unity's first
  `Start` messages. The command mod registers commands there so the HotRepl host and Unity-side
  systems have had their normal initialization opportunity.
  <https://github.com/LavaGang/MelonLoader/blob/master/MelonLoader/Melons/MelonBase.cs>

## 3. Scope

### In scope

- Add a new MelonLoader mod under `mods/HotReplCommands/` that registers Ancient Kingdoms commands
  with `GlobalControlCommandRegistry`.
- Register these typed commands, all at major version `1`:
  - `compendium.preflight`
  - `world.summary`
  - `compendium.export`
  - `game.quit`
- Move the world-entry automation currently owned by `AutoExporter` into command-owned code: Start
  scene -> click singleplayer -> World scene -> select the first character -> start local world ->
  wait for `Il2CppMirror.NetworkClient.localPlayer` -> settle.
- Keep `DataExporter` responsible for data serialization and manual Shift+F9 export.
- Keep `MapScreenshotter` responsible for screenshot capture and manual Shift+F10 capture, but
  expose an explicit capture result/error that the command job can observe.
- Replace `build-tool export` with a HotRepl WebSocket orchestration path that launches the game
  without export flags, waits for command readiness, invokes typed commands, polls the export job,
  verifies artifact refs, and requests graceful quit.
- Delete `AutoExporter`, `ExportResultFile`, `ExportResultReader`, and tests dedicated only to the
  result-file side channel.
- Update Ancient Kingdoms docs and skills so `build-tool export` is the only automated export entry
  point.
- Update HotRepl roadmap/spec/plan files as implementation decisions land.

### Out of scope

- Adding new Ancient Kingdoms entity exporters, build-pipeline loaders, website pages, or data model
  fields.
- Migrating unrelated gameplay/helper mods to HotRepl commands.
- Replacing the Python build pipeline or SvelteKit website.
- Adding protocol-level auth, leases, idempotency keys, or per-command permission gates. HotRepl's
  v2/v3 authority boundary remains loopback plus single-client replacement.
- Publishing HotRepl packages or introducing repo-local NuGet package feeds for this migration.
- Rewriting `DataExporter` internals beyond the changes needed to remove `.exporter-result.json` and
  expose command-consumable export status.

## 4. Approach decision

### 4.1 Rejected: patch launch flags and keep the result file

Keeping `AutoExporter` and `.exporter-result.json` would minimize code churn, but it would not
validate typed commands, job progress, command schemas, named artifacts, or the WebSocket client
path. It would also leave two export surfaces after `build-tool export` moves to HotRepl.

### 4.2 Rejected: shell out from build-tool to `@hotrepl/cli`

Using the TypeScript CLI would reduce protocol code in C#, but it would add a Node/Bun runtime
requirement to a .NET build-tool command that currently owns game launch/export. It would also make
artifact verification and process supervision harder to test without subprocess fixtures.

### 4.3 Chosen: command mod plus raw C# WebSocket runner

Add a small typed-command mod and a focused `ClientWebSocket` runner in `build-tool`. This keeps the
existing .NET export command as the user-facing entry point, removes the launch-flag completion side
channel, and exercises the protocol directly in the IL2CPP consumer. The runner is deliberately
narrow: it supports only the messages needed for AK export, not a general HotRepl SDK.

## 5. Design

### 5.1 Mod ownership

`mods/HotReplCommands/` owns command registration and orchestration. It references:

- `DataExporter` for data export;
- `MapScreenshotter` for optional screenshot capture;
- `HotRepl.Core.dll` for typed command APIs and bundled schema generation (NJsonSchema and
  `Namotion.Reflection` are ILRepack-internalized; no Namotion sidecar deploys).

The command mod registers in `OnLateInitializeMelon` to avoid racing MelonLoader and HotRepl host
initialization. Registration is idempotent in process lifetime: commands are registered once and are
not hot-reloaded in place.

`DataExporter` remains the source of truth for exported JSON and selected visual images. Manual
Shift+F9 export stays. `MapScreenshotter` remains the source of truth for screenshot capture. Manual
Shift+F10 capture stays. `HotReplCommands` composes them; it does not duplicate exporter logic.

`AutoExporter` is deleted. Automated exports no longer depend on `--export-data` or
`--export-screenshots` process arguments.

### 5.2 Command inventory

| Command                | Args type              | Output type              | Kind | Mutates |
| ---------------------- | ---------------------- | ------------------------ | ---- | ------- |
| `compendium.preflight` | `EmptyArgs`            | `PreflightResult`        | sync | no      |
| `world.summary`        | `EmptyArgs`            | `WorldSummaryResult`     | sync | no      |
| `compendium.export`    | `CompendiumExportArgs` | `CompendiumExportResult` | job  | yes     |
| `game.quit`            | `EmptyArgs`            | `GameQuitResult`         | sync | yes     |

`compendium.preflight` verifies that the command mod can see required collaborators and paths:
`DataExporter`, `MapScreenshotter`, export directory, screenshot directory, current scene, character
selection availability, and local-player readiness. It reports readiness facts; it does not click
UI, load a world, export files, or create directories beyond what the existing mods already create.

`world.summary` is diagnostic. It returns the active scene, network/lobby state when available,
character count when available, selected character when available, and whether a local player
exists. It is safe for agents to call before deciding whether to run export.

`compendium.export` is the only automated export job. It performs the full export workflow:

1. report `enteringWorld` progress;
2. if not already world-ready, execute the former `AutoExporter` world-entry sequence;
3. report `exportingData` progress;
4. call `DataExporter.ExportAllData()`;
5. if `screenshots` is true, report `capturingScreenshots` progress and call `MapScreenshotter`;
6. collect named artifact references for JSON, visual assets, images, and optional screenshots;
7. return `CompendiumExportResult` plus the top-level artifact map.

The command does not call `Application.Quit()`. It must send its terminal `job_result` first.
`build-tool export` calls `game.quit` after it has received and verified the terminal result. If the
quit command cannot complete, build-tool reports the export result and the quit failure separately.

`game.quit` calls `UnityEngine.Application.Quit()` and returns `{ "quitting": true }` before the
process exits.

### 5.3 DTO and schema rules

All command DTOs are ordinary POCOs with explicit lower-camel Newtonsoft metadata:

```csharp
public sealed class CompendiumExportArgs
{
    [JsonProperty("screenshots", Required = Required.Always)]
    public bool Screenshots { get; set; }
}
```

Output DTOs follow the same rule. Required output fields use `Required.Always`; nullable output
fields use `Required.AllowNull`. Required property presence is schema/server validation. Domain
validation stays in handlers and returns typed failures, for example:

- `preflightFailed`
- `worldEntryUnavailable`
- `characterMissing`
- `dataExporterMissing`
- `mapScreenshotterMissing`
- `screenshotCaptureFailed`
- `artifactMissing`

The mod does not add `System.ComponentModel.DataAnnotations` or `System.Text.Json` for this
migration. Newtonsoft metadata is enough to generate required schema fields through HotRepl's
NJsonSchema-based schema cache.

### 5.4 World-entry workflow

The command-owned world-entry automation preserves the current `AutoExporter` semantics:

- in `Start`, wait one frame for `UILogin`, then invoke `singlePlayerButton`;
- in `World`, wait for `UICharacterSelection.singleton`;
- wait for lobby state and `charactersAvailableMsg.characters`;
- fail with a precondition diagnostic when no character exists;
- select the first character;
- set `NetworkManagerMMO.name_character_selected`;
- set `PlayerPrefs["selected_char"]`;
- set `<character>_intro_run = 1`;
- call `ClearPreviews()`;
- call `UIServerList.singleton.StartConnect(null)`;
- wait for `Il2CppMirror.NetworkClient.localPlayer`;
- wait a short settle period before export.

The implementation uses `async`/`await`, `Task.Yield()`, and bounded waits on HotRepl's main-thread
execution path. It checks `CancellationToken` in every wait loop. It never uses background threads
for Unity APIs.

### 5.5 Progress and cancellation

`compendium.export` reports coarse progress through `ControlCommandContext.Progress`:

```json
{ "phase": "enteringWorld", "message": "Selecting first character." }
{ "phase": "exportingData", "message": "Running DataExporter." }
{ "phase": "capturingScreenshots", "current": 17, "total": 180 }
{ "phase": "collectingArtifacts", "message": "Hashing exported files." }
```

Screenshot progress can be coarse if `MapScreenshotter` cannot cheaply report per-tile progress in
the first implementation. It must at least distinguish started, completed, and failed states.

Cancellation is cooperative. A canceled job stops before starting the next phase when possible. If
cancellation occurs while `MapScreenshotter` is mid-capture and the existing capture code cannot
stop safely without risking corrupted Unity state, the command reports cancellation as soon as the
current capture loop observes a safe stop point. The spec does not require a hard abort.

### 5.6 Artifact map

`compendium.export` returns artifact refs at the HotRepl top level, not inside the output DTO. The
collector scans known export outputs after the job finishes and returns a deterministic
`Dictionary<string, ArtifactRef>(StringComparer.Ordinal)`.

Stable artifact key rules:

- static JSON exports: `data.<file-stem>` (for example `data.monsters`, `data.monster-spawns`,
  `data.classes-combat`);
- visual asset manifest: `visual-assets.manifest`;
- visual image files: `visual-assets.image.<relative-path-without-extension>`;
- screenshot metadata: `screenshots.metadata`;
- screenshot PNG files: `screenshots.<file-stem>`.

Every artifact ref includes `uri`, `path`, `sha256`, `byteSize`, `contentType`, and `finalized`.
Artifact bytes are never embedded in command output. Missing required static export artifacts fail
the job with `artifactMissing`. Optional screenshots are required only when `screenshots` is true.

### 5.7 Build-tool WebSocket runner

`build-tool/HotRepl/HotReplExportRunner.cs` is a narrow protocol client for the export path. It uses
`ClientWebSocket` and Newtonsoft.Json. It does not become a general SDK.

Runner sequence:

1. connect to `ws://127.0.0.1:18590` by default, with an option to override the URL;
2. wait for `handshake`;
3. reject any `protocolVersion` other than `2`;
4. send `commands_list` and wait for `commands_list_result`;
5. verify all required command names and major versions;
6. optionally call `command_describe` in tests and diagnostics to assert schema availability;
7. call `compendium.preflight`;
8. call `world.summary` when preflight reports a non-ready world, for diagnostics;
9. call `compendium.export` with `{ "screenshots": <bool> }`;
10. poll `job_status` until it returns terminal `job_result`;
11. verify returned artifact refs;
12. call `game.quit` after a successful terminal export result.

The runner serializes sends and receives. It has one receive loop and does not issue multiple
parallel `ReceiveAsync` calls or multiple parallel `SendAsync` calls on the same socket.

The runner explicitly does not send:

- `control_auth`
- `lease_acquire`
- `ping`
- profile messages
- client-originated `job_result`

`build-tool export` launches the game without `--export-data` or `--export-screenshots`. Because the
WebSocket may accept connections before commands are registered, the runner retries readiness
through `commands_list` until its bounded timeout expires.

### 5.8 Exit-code vocabulary

Ancient Kingdoms build-tool retains numeric categories but removes obsolete auth/lease names:

| Code | Name               | Meaning                                          |
| ---- | ------------------ | ------------------------------------------------ |
| 0    | `Success`          | Export completed and artifacts verified          |
| 1    | `Internal`         | Unexpected build-tool or protocol bug            |
| 2    | `InvalidUsage`     | Invalid CLI args or validation failure           |
| 3    | `Unreachable`      | Game, HotRepl endpoint, or external tool missing |
| 4    | `PermissionFailed` | Reserved for OS/file permission failures         |
| 5    | `ResourceConflict` | File lock, busy export, or resource conflict     |
| 6    | `ReadinessFailed`  | Timed out waiting for game/command readiness     |
| 7    | `CommandFailed`    | HotRepl command/job failed                       |
| 8    | `Cancelled`        | User cancellation                                |

Protocol error kinds map into these categories. `auth_failed`, `lease_conflict`, and
`lease_required` are not part of the v3 AK export flow and are not documented as expected outputs.

### 5.9 Deployment

`build-tool deploy-host` continues to build HotRepl's MelonLoader host from a configurable HotRepl
checkout. It must deploy the host, `HotRepl.Core.dll`, and Core's consumer-facing dependencies
(`HotRepl.Protocol.dll`, `Newtonsoft.Json.dll`, `Fleck.dll`) plus the host's evaluator sidecars into
the game `Mods/` directory. Tests must assert that no `Namotion.Reflection.dll` ever lands in the
deployed plugin folder: HotRepl Core 3.0.0 internalizes Namotion via ILRepack, so a Namotion sidecar
in the deploy output is a regression.

`mods/HotReplCommands/HotReplCommands.csproj` references `HotRepl.Core.dll` from a configurable
HotRepl output path. Defaults may assume the HotRepl repo is a sibling checkout, but the path must
be overridable from `Local.props` and must not be hardcoded to one user's home directory.

### 5.10 Documentation updates

The Ancient Kingdoms docs are updated in the implementation commit-set:

- `CLAUDE.md`
- `README.md`
- `mods/CLAUDE.md`
- `mods/DataExporter/CLAUDE.md`
- `mods/MapScreenshotter/CLAUDE.md`
- `.claude/skills/hotrepl-runtime-inspection/SKILL.md`
- `docs/data-export-guide.md`
- `docs/project-map.md`

Docs must state that:

- `build-tool export` is the automated export entry point;
- `AutoExporter`, `--export-data`, and `.exporter-result.json` are gone;
- `DataExporter` owns serialization and manual Shift+F9;
- `MapScreenshotter` owns screenshot capture and manual Shift+F10;
- HotRepl commands are discoverable through `commands_list` / `command_describe`;
- default HotRepl bind remains loopback-only.

## 6. Test strategy

### 6.1 HotReplCommands mod tests

Add or update Unity-free tests where possible by factoring DTO/catalog/collector logic into files
that can compile outside the game process.

Required coverage:

- command catalog names, versions, kinds, and mutation flags;
- DTO schema generation lower-camel names and required fields;
- artifact collector key stability, byte sizes, content types, and SHA-256 values using temp files;
- missing required artifact produces a typed failure;
- `CompendiumExportArgs.screenshots` is required in generated input schema.

Game-object automation remains covered by live validation because it depends on IL2CPP runtime
state. The code should still isolate pure decisions (state classification, timeout messages, output
DTOs) for ordinary unit tests.

### 6.2 DataExporter tests

Update `tests/DataExporter.Tests/` after deleting `ExportResultFile`:

- keep `ExportRunResult` JSON round-trip coverage for command output use;
- remove `ExportResultFileTests`;
- assert `ExportRunResult` remains serializable without writing `.exporter-result.json`;
- if exporter result shape changes, assert required Newtonsoft property names.

### 6.3 MapScreenshotter tests

Add Unity-free coverage for any new result DTO that describes screenshot completion:

- success result includes metadata path and tile count;
- failure result includes stable error kind/code/message;
- starting capture while already capturing reports a conflict instead of silently succeeding.

### 6.4 Build-tool tests

Required build-tool coverage:

- `HotReplExportRunner` consumes `handshake`, rejects mismatched protocol versions, sends
  `commands_list`, calls `command_call`, polls `job_status`, and accepts terminal `job_result` from
  `job_status`;
- runner does not send auth, lease, ping, profile, or client `job_result` messages;
- command readiness retries after connection/handshake while `commands_list` is not yet responsive;
- artifact verification rejects missing paths, non-finalized refs, missing required keys, and
  zero-byte required files;
- `ExportCommand` launches without `--export-data` or `--export-screenshots`;
- `LaunchCommand --export` is removed, or tests assert it is rejected if the option remains only
  long enough for command-line parser cleanup;
- exit-code mappings use `ResourceConflict` / `ReadinessFailed` vocabulary rather than lease/auth
  vocabulary;
- deploy-host does not copy `Namotion.Reflection.dll`; HotRepl Core 3.0.0 internalizes it, so its
  presence in the host output is a regression.

Fake WebSocket tests should use an in-memory protocol seam or loopback test server. No test starts
the real game.

### 6.5 Local gates

Before the AK implementation is claimed complete, run the narrow gates that cover touched code:

```bash
dotnet test tests/BuildTool.Tests/ --nologo -v q
dotnet test tests/DataExporter.Tests/ --nologo -v q
dotnet run --project build-tool build
```

When local game configuration is available, also run:

```bash
dotnet run --project build-tool deploy-host
dotnet run --project build-tool deploy
dotnet run --project build-tool export --json
dotnet run --project build-tool export --screenshots --json
cd build-pipeline && uv run compendium build
cd build-pipeline && uv run compendium tiles
cd website && pnpm check && pnpm lint && pnpm build
```

If a live gate cannot run because the local game, Wine/CrossOver, Unity assemblies, or HotRepl
output is unavailable, the implementation closeout records the missing prerequisite and every
non-live gate that did run.

## 7. Acceptance criteria

- Ancient Kingdoms builds against `HotRepl.Core` 3.0.0 typed command APIs with no references to the
  deleted non-generic command handler shape.
- `mods/HotReplCommands` registers `compendium.preflight`, `world.summary`, `compendium.export`, and
  `game.quit` as typed commands with generated input/output schemas.
- `compendium.export` is a job, reports progress, honors cancellation checks, and returns terminal
  output plus top-level named artifact refs.
- `AutoExporter`, `.exporter-result.json`, `ExportResultFile`, and `ExportResultReader` are removed.
- `build-tool export` does not pass `--export-data` or `--export-screenshots`; it drives the export
  through HotRepl WebSocket messages.
- `build-tool` does not send auth, lease, ping, profile, or client `job_result` messages.
- `game.quit` is invoked after successful export result delivery; process-level cleanup remains a
  build-tool fallback for launch/export failures.
- HotRepl host deployment ships no `Namotion.Reflection.dll` sidecar; the merged `HotRepl.Core.dll`
  carries internalized NJsonSchema and Namotion.
- Ancient Kingdoms docs and HotRepl roadmap/spec/plan files reflect the new flow.
- Local unit/build gates pass, and live export evidence is recorded when the local game environment
  is available.

## 8. Risks and mitigations

- **Game readiness races:** A WebSocket connection can exist before commands are registered. The
  runner waits for `handshake` and retries `commands_list` until the command catalog is available.
- **Unity main-thread misuse:** World entry and export logic stays on HotRepl's main-thread tick
  path. No Unity API is called from a background thread.
- **Screenshot failure ambiguity:** `MapScreenshotter` currently exposes only `IsCapturing`. The
  migration adds an explicit result/error surface before command orchestration depends on it.
- **Artifact map size:** Exporting visual images and screenshots can produce many artifact refs. The
  refs are metadata only, deterministic, and cheaper than embedding payloads. Build-tool validates
  required refs without re-hashing every file by default.
- **Packaging drift:** Tests assert `Namotion.Reflection.dll` stays absent from the deployed plugin
  folder so a future ILRepack regression cannot silently reintroduce a sidecar dependency.
- **Wider-than-loopback deployment:** Because HotRepl has no auth/lease protocol, the default stays
  `127.0.0.1`. Documentation warns that broader binding requires a trusted external network
  boundary.
- **Command-induced quit before result delivery:** Export and quit are separate commands. The export
  job returns first; `build-tool` calls `game.quit` only after receiving and verifying the terminal
  result.
