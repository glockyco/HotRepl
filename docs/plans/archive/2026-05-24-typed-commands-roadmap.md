---
title: "Typed commands — roadmap"
type: spec
status: implemented
created: 2026-05-24
parent:
superseded_by:
archived: 2026-06-25
---

# Typed commands — roadmap

A cross-repo design and delivery roadmap for replacing HotRepl's hand-written-`JObject`-schema
command pattern with a strongly-typed interface that derives schemas from the C# argument and result
types.

## Why

HotRepl already positions typed commands as "the stable contract for repeatable workflows, agents,
and CI." The repo's own README pitches them that way. But every typed command in the wild today is
written against the v2 interface (`IControlCommandHandler` returning `JObject` with a hand-written
`JObject` schema literal), which has three problems:

1. **Schema drift.** The schema and the args type are two unrelated sources of truth. They diverge
   silently. Agents see one shape; the handler accepts another. Ardenfall has hit this multiple
   times.
2. **Anemic agent surface.** Most existing handlers advertise `ArgsSchema = AnyObject`, so
   `tools/list` outputs are useless to MCP-driven agents. The schema mechanism exists but nobody
   fills it in because authoring is painful.
3. **No first-party reference.** New plugin authors must read Ardenfall to learn the
   command-authoring pattern. There is no scaffold, no demo catalog, no template. The strategic
   effect is that HotRepl is hard to pick up.

The work below addresses all three. It also fixes a latent gap surfaced during design: today's
`IControlCommandHandler` job handlers cannot report progress through the public API (the router
strips the progress callback when building the public `ControlCommandContext`). The new interface
restores it.

## What we're shipping

Six coordinated workstreams across five repos, structured as six phases. Phases 2–6 each get their
own detail spec written *when that phase starts*, not now (Phases 1–4 already have specs). The
roadmap below names them, fixes their order, and locks the architectural decisions that constrain
later phases.

| Phase                   | Scope                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      | Spec                                                                                                                                 |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| **1. Foundation**       | `HotRepl.Core` typed-command interface + result wrapper + context + schema generation; new `HotRepl.UnityCommands` first-party demo plugin; new `glockyco/hotrepl-mod-template` GitHub repo. Bump `HotRepl.Core` to 3.0.0 (clean break, no backward compat shim).                                                                                                                                                                                                                          | [`2026-05-24-typed-commands-phase-1-foundation.md`](2026-05-24-typed-commands-phase-1-foundation.md) (detailed, ready)               |
| **2. Ardenfall**        | **Done.** Migrated Ardenfall's 10 commands to the new interface. Simplest real consumer (Mono, synchronous commands plus one existing batch-export job pattern, no IL2CPP). Fastest design-validation loop.                                                                                                                                                                                                                                                                                | [`2026-05-24-typed-commands-phase-2-ardenfall-migration.md`](2026-05-24-typed-commands-phase-2-ardenfall-migration.md)               |
| **3. Ancient Kingdoms** | **Done.** Deleted `AutoExporter` and the `.exporter-result.json` round-trip; added new `HotReplCommands` mod with job-pattern commands for world entry + export + screenshots; rewrote `build-tool export` to drive over WebSocket. Validates IL2CPP, jobs, multi-frame coroutine workflows, and multi-named-artifact returns.                                                                                                                                                             | [`2026-05-24-typed-commands-phase-3-ancient-kingdoms-migration.md`](2026-05-24-typed-commands-phase-3-ancient-kingdoms-migration.md) |
| **4. Consolidation**    | Authoring-API refinement (`ControlCommandContext<TOutput>` + instance helpers, `[ControlCommand]` attribute, `Synchronous` → `Sync` rename, validator caching, capability honesty) + first-party `HotRepl.Sdk` (netstandard2.0) + `HotRepl.Testing` package + first-class file artifacts via `IArtifactWriter.AttachFileAsync` + catalog caching in TS+C# SDKs + sample/docs promotion. Wire stays. Three sub-plans: 4a HotRepl-internal, 4b Ardenfall update, 4c Ancient Kingdoms update. | [`2026-05-24-typed-commands-phase-4-consolidation.md`](2026-05-24-typed-commands-phase-4-consolidation.md) (detailed, ready)         |
| **5. Erenshor**         | Replace `MapTileCapture`'s bespoke Fleck WebSocket server with typed HotRepl commands; update the Python pipeline to shell out to `bunx @hotrepl/cli`. Validates long-running streaming, Python consumer integration, exclusion-rule arg shapes. Built on the Phase 4 SDK + Testing surface.                                                                                                                                                                                               | TBD when phase starts                                                                                                                |
| **6. Document**         | README, AGENTS.md, landing page, per-package READMEs all reflect the new pattern. Update commit conventions if needed.                                                                                                                                                                                                                                                                                                                                                                     | TBD when phase starts                                                                                                                |

## Phase order rationale

**Phase 1 first**: every other phase consumes the API defined here. We need it stable before
downstream migrations start. But Phase 1 isn't a greenfield design — it's constrained by what Phases
2–4 will demand, which we lock as architectural requirements before writing Phase 1's detail spec
(see "Cross-phase constraints" below).

**Phase 2 (Ardenfall) before Phase 3 (AK)**: Ardenfall is the simplest real consumer. Mono, no
IL2CPP. Sync commands plus a few job commands that already have established progress/cancel shapes
in Ardenfall's build-tool — the migration is structural, not semantic. Running Ardenfall first
validates the typed-interface mechanics under real conditions before stressing them against AK's
harder workflows.

**Phase 3 (AK) before Phase 4 (Consolidation)**: AK is the hardest v3 consumer. IL2CPP runtime,
multi-frame coroutine workflows (`AutoExporter`'s scene-state state machine), multi-named-artifact
returns (DataExporter's manifest items + asset-manifest + tooltip + categories + tags +
diagnostics). Running AK on v3 surfaced the real-world friction (duplicated WebSocket clients,
`<TOutput>` boilerplate, ad-hoc artifact helpers, wire/CLR enum mismatch) that Phase 4 fixes. The v3
design survived AK; Phase 4 distills the lessons into a refined authoring API and the missing C#
SDK.

**Phase 4 (Consolidation) before Phase 5 (Erenshor)**: Erenshor is the third real consumer. We want
Erenshor built on the Phase 4 surface (`HotRepl.Sdk`, `HotRepl.Testing`, the refined context API,
`IArtifactWriter.AttachFileAsync`) — not on the same v3 friction patterns AK and Ardenfall absorbed.
Phase 4 includes mechanical rebuilds of Ardenfall and AK against the new APIs as sub-plans (4b, 4c)
to keep the design honest. Erenshor adds the streaming-progress shape (chunk-by-chunk capture
progress to the Python driver) but is otherwise simpler than AK.

**Phase 6 (Document)** runs in parallel with whichever phase is current once the API surface stops
moving. Likely starts mid-Phase 4.

## Cross-phase constraints (locked now, honored in every phase spec)

These constrain the API shape and cannot be deferred to "the plan will verify." They came out of
architectural review of an earlier draft; locking them as roadmap-level requirements stops the same
questions re-surfacing in each phase spec.

1. **No backward compatibility.** Each major version is a clean break. v2 → v3 retired the pre-typed
   `IControlCommandHandler` returning `JObject` (Phase 1). v3 → v4 retires the
   `ControlCommandResult.<Method><TOutput>` static failure factories and the wire/CLR-mismatched
   `ControlCommandKind.Synchronous` (Phase 4). `HotRepl.Core` will ship at 4.0.0 after Phase 4.
   Every controlled consumer migrates within this work; don't ship parallel compat-shim interfaces.

2. **Typed result wrapper carries output + artifacts + diagnostics + status.** Today's
   `ControlCommandResult` already carries all three non-output fields at the top level (wire shape:
   `output`, `artifacts` map, `diagnostics`). The typed surface must preserve that wire shape while
   letting handlers express it ergonomically. Stuffing artifact refs inside the typed `TOutput` POCO
   is forbidden — it breaks wire compatibility with clients that read top-level `artifacts`.

3. **Progress and cancellation are first-class on the context for job commands.** Today's public
   `ControlCommandContext` strips the progress callback even for job handlers. The new typed context
   exposes `IProgress<JObject?>` for progress and forwards a `CancellationToken` parameter
   alongside.

4. **Multi-frame Unity workflows must work.** Unity's `UnitySynchronizationContext` already posts
   `await` continuations back to the main thread on the next frame, so plain `async/await` with
   `Task.Yield()` is sufficient — no custom coroutine bridge is needed in the Core API. Phase 3's AK
   migration will validate this end-to-end (scene-load wait, character-select wait, player-spawn
   wait).

5. **Server-side validation runs in the adapter, before the handler sees args.** Schemas surface to
   MCP clients and the server validates inbound args against them too. Validation failures return a
   `validation_failed` diagnostic in the result wrapper, NOT an exception. This protects handlers
   from having to redo argument validation in prose, and gives MCP clients a stable error shape.

6. **Schema generation via NJsonSchema 10.9.0, with `NJsonSchema.dll` and its `Namotion.Reflection`
   dependency internalized into `HotRepl.Core.dll`.** Pinning to 10.9.0 (the last release before the
   `System.Text.Json` transitive dependency) keeps the runtime surface Newtonsoft-only — known-good
   on Mono Unity. ILRepack merges both with
   `AllowedDuplicateNamespaces="System.Runtime.CompilerServices"` so `Namotion.Reflection`'s
   `IsExternalInit` polyfill folds into Core without renaming Core's canonical `IsExternalInit`
   metadata. Downstream consumers ship `HotRepl.Core.dll` with `Newtonsoft.Json.dll` and `Fleck.dll`
   only — no Namotion sidecar.

7. **Plugin layout: shared-source compilation, no `Core` csproj with Unity dependency.**
   UnityCommands' command bodies live in a source folder that two loader-specific csprojs each
   `<Compile Include>` against their own `UnityEngine.dll` reference. The mod template uses the same
   layout. No `MyMod.Core` csproj with Unity dependencies — that would force a choice between Mono
   and IL2CPP-unhollowed flavors of `UnityEngine.dll`. Unity-free catalog metadata is kept in
   separate shared-source files so ordinary .NET CI can verify command names/kinds/mutation flags
   without checked-in Unity assemblies; loader builds remain the authority for Unity API
   compilation.

8. **MelonLoader bootstrap via `OnLateInitializeMelon`.** Introduced in 0.6, fires after every
   `OnInitializeMelon` across all mods — guaranteed to run after
   `GlobalControlCommandRegistry.Instance` is populated by the HotRepl host. `OnApplicationStart` is
   removed in MelonLoader 0.7.0, so don't use it.

9. **Migrations validate the design, not the design's afterthought.** AK and Erenshor in particular
   stress patterns the foundation doesn't exercise (multi-frame Unity workflows, named-artifact
   maps, Python consumer flows). Their detail specs may surface API gaps; if they do, the foundation
   spec is amended and the migration spec is rewritten against the new API. The roadmap admits this
   loop. Phase 4's Ardenfall + AK sub-plans (4b/4c) follow the same principle — rebuilding both real
   consumers against the v4 API surface validates that the refinements actually pay off in
   production code.

10. **Wire protocol is stable from v3 onwards.** The `protocolVersion: 2` handshake, message types,
    descriptor and artifact-ref shapes are locked. Phase 4 refines the C#/TypeScript SDK surface and
    the C# authoring API but **does not change the wire**. Capability flags that were lying (e.g.
    `schemaValidation: false` when the server actually did validate) are corrected in Phase 4 to
    report truth; client-visible behaviour does not change.

11. **First-party SDKs are the only blessed automation surface.** From Phase 4 onwards, C# and
    TypeScript consumers MUST drive HotRepl through `HotRepl.Sdk` (C#) or `@hotrepl/sdk` (TS).
    Hand-rolled `JsonDocument`/`ClientWebSocket` clients (as previously seen in AK's
    `HotReplExportRunner.cs` and Ardenfall's `hotrepl-client.ts`) are deleted, not maintained in
    parallel. Future C# build-tools, test harnesses, or CLIs ride on `HotRepl.Sdk`.

12. **File artifacts are a first-class core capability.** From Phase 4 onwards, handlers attach
    files via `context.Artifacts.AttachFileAsync(logicalName, path, contentType)`. The writer owns
    SHA-256 hashing, byte-size, URI stamping, and finalization. Consumer-side helpers
    (`ArtifactCollector.MakeRef`, `FileArtifact`) are deleted. Declared artifact keys surface to
    `commands.describe` clients via `[ControlCommandArtifact]` on the handler.

## Open questions (to resolve during phase-spec writing)

These can't be decided abstractly — they need the context of the specific phase that hits them.

- **AK `DataExporter.ExportAllData()` refactor for progress reporting.** Current implementation is
  synchronous, no progress hooks. To report per-exporter progress through the new typed context,
  DataExporter needs internal refactoring. The Phase 3 spec deferred this; Phase 4c (AK update) will
  revisit alongside the `HotRepl.Sdk` adoption.
- **Erenshor Python consumer pattern.** `bunx @hotrepl/cli run ...` shell-out works but loses
  streaming progress unless the CLI exposes a way to consume job events. The Phase 5 spec picks:
  terminal-only with documented tradeoff vs. CLI subcommand that prints job events to stdout as they
  arrive.
- **AK game-quit mechanism.** Resolved in Phase 3 by adding a typed `game.quit` command invoked by
  the build-tool after successful export.
- **Mod template `dotnet new` parameters.** Which substitutions the template manifest exposes
  (plugin GUID, author name, mod name) — was a Phase 1 decision, locked.
- **`[ControlCommandArtifact]` attribute vs
  `static IReadOnlyDictionary<string, ArtifactSpec>
  ArtifactKeys` convention.** Phase 4 spec leans
  toward attribute; revisit if a consumer with computed/dynamic catalogs needs the dictionary form.
- **`HotRepl.Sdk` typed-args overloads.** Whether to ship both `RunAsync<TArgs, TResult>` (typed)
  and `RunAsync<TResult>(string, IReadOnlyDictionary<string, object?>)` (untyped) for callers
  without a shared DTO assembly. Decide during Phase 4 implementation based on AK build-tool
  call-site shape.

## What is explicitly NOT in scope

| Out of scope                                   | Why                                                                                                                                                                                            |
| ---------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Hot config reload at runtime                   | Mid-flight `GlobalControlCommandRegistry` mutation while jobs are in flight is hard to get right. Restart-required is fine.                                                                    |
| A second non-Unity demo plugin                 | Premature. Demonstrate Unity well first.                                                                                                                                                       |
| Publishing the template to NuGet               | GitHub-template-button covers the discoverability win. NuGet publish is a future PR.                                                                                                           |
| Publishing UnityCommands to Thunderstore       | Wrong audience — Thunderstore is for end-user game mods, UnityCommands is developer tooling.                                                                                                   |
| Migrating Erenshor's `InteractiveMapCompanion` | Per maintainer call; it's a live-state streamer, not export-shaped.                                                                                                                            |
| Migrating AK's other 11 mods                   | Out of scope per maintainer call; they're gameplay tweaks, not command-shaped.                                                                                                                 |
| Per-command rate limiting / permission gates   | Loopback + single-client is HotRepl's authority model. Per-command policy is YAGNI.                                                                                                            |
| JSON-RPC 2.0 / StreamJsonRpc wire pivot        | Investigated and rejected in Phase 4 design. Mandatory MessagePack/Nerdbank/STJ dependencies, IL2CPP-hostile dynamic proxies, no JSON Schema contract, jobs aren't native. Solves wrong layer. |
| `JsonSchema.Net.Generation` schema replacement | STJ-centric, ignores Newtonsoft attrs, OSMF EULA. The actual NJsonSchema cost is fixed in Phase 4 by caching compiled validators.                                                              |
| Source generator for handler metadata          | Hostile to BepInEx/MelonLoader HintPath workflows. `[ControlCommand]` runtime attribute provides the same ergonomics with explicit, grep-friendly metadata.                                    |
| Built-in automatic reconnect in the SDK        | HotRepl's single-client/session-eviction model makes silent reconnect unsafe. Opt-in policy can be added later if a consumer needs it.                                                         |

## Done means

- Every published HotRepl package (`@hotrepl/protocol`, `@hotrepl/sdk`, `@hotrepl/cli`,
  `@hotrepl/mcp`) is on v3 of the protocol with the new command shape.
- `HotRepl.Core` ships at 4.0.0 after Phase 4 (3.0.0 was Phase 1's clean break; 4.0.0 is Phase 4's
  authoring-API refinement). NJsonSchema and Namotion.Reflection remain ILRepack-internalized.
  Downstream consumers deploy `HotRepl.Core.dll`, `HotRepl.Protocol.dll`, `Newtonsoft.Json.dll`, and
  `Fleck.dll`; no Namotion sidecar.
- `HotRepl.Sdk` (netstandard2.0) and `HotRepl.Testing` (netstandard2.0) ship as new NuGet packages
  after Phase 4. Build-tools, automation, and consumer test projects use these instead of
  hand-rolled protocol clients.
- `HotRepl.UnityCommands` ships 4 demo commands (BepInEx + MelonLoader variants) and shows up in
  `commands_list` on every HotRepl install using bundled deployment. After Phase 4, these packages
  are labelled canonical samples.
- `glockyco/hotrepl-mod-template` exists, builds, deploys, and a fresh fork produces a working
  "hello world" mod within 5 minutes.
- Ardenfall, AK, and Erenshor are on the new pattern. Their existing HotRepl-driven workflows
  (`bun run hotrepl:setup`, `build-tool export`, the Python map-capture pipeline) keep working with
  no user-facing regressions. After Phase 4, Ardenfall and AK use `HotRepl.Sdk` and
  `context.Artifacts.AttachFileAsync`; their hand-rolled WS clients are deleted.
- Both SDKs (`@hotrepl/sdk` and `HotRepl.Sdk`) cache `commands_list` per session and only call
  `command_describe` when a caller explicitly requests a schema.
- `docs/authoring-commands.md` exists and is referenced from README, AGENTS.md, and the landing page
  as the canonical authoring guide.
- README, AGENTS.md, the landing page, and per-package READMEs reflect the new typed-command pattern
  as the canonical way to author HotRepl commands.
- `lefthook run pre-push --force` clean across every touched repo.
