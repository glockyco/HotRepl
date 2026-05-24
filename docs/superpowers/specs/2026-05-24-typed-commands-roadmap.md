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

Five coordinated workstreams across five repos, structured as five phases. Phases 2–5 each get their
own detail spec written *when that phase starts*, not now. The roadmap below names them, fixes their
order, and locks the architectural decisions that constrain later phases.

| Phase                   | Scope                                                                                                                                                                                                                                                                                                             | Spec                                                                                                                   |
| ----------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| **1. Foundation**       | `HotRepl.Core` typed-command interface + result wrapper + context + schema generation; new `HotRepl.UnityCommands` first-party demo plugin; new `glockyco/hotrepl-mod-template` GitHub repo. Bump `HotRepl.Core` to 3.0.0 (clean break, no backward compat shim).                                                 | [`2026-05-24-typed-commands-phase-1-foundation.md`](2026-05-24-typed-commands-phase-1-foundation.md) (detailed, ready) |
| **2. Ardenfall**        | Migrate Ardenfall's ~10 typed commands to the new interface. Simplest real consumer (Mono, no jobs, no IL2CPP). Fastest design-validation loop.                                                                                                                                                                   | TBD when phase starts                                                                                                  |
| **3. Ancient Kingdoms** | Delete `AutoExporter` and the `.exporter-result.json` round-trip; add new `HotReplCommands` mod with job-pattern commands for world entry + export + screenshots; rewrite `build-tool export` to drive over WebSocket. Validates IL2CPP, jobs, multi-frame coroutine workflows, and multi-named-artifact returns. | TBD when phase starts                                                                                                  |
| **4. Erenshor**         | Replace `MapTileCapture`'s bespoke Fleck WebSocket server with typed HotRepl commands; update the Python pipeline to shell out to `bunx @hotrepl/cli`. Validates long-running streaming, Python consumer integration, exclusion-rule arg shapes.                                                                  | TBD when phase starts                                                                                                  |
| **5. Document**         | README, AGENTS.md, landing page, per-package READMEs all reflect the new pattern. Update commit conventions if needed.                                                                                                                                                                                            | TBD when phase starts                                                                                                  |

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

**Phase 3 (AK) before Phase 4 (Erenshor)**: AK is the hardest. IL2CPP runtime, multi-frame coroutine
workflows (`AutoExporter`'s scene-state state machine), multi-named-artifact returns (DataExporter's
manifest

- items + asset-manifest + tooltip + categories + tags + diagnostics). If the API survives AK, it
  survives almost anything. Erenshor adds the streaming-progress shape (chunk-by-chunk capture
  progress to the Python driver) but is otherwise simpler than AK.

**Phase 5 (Document)** runs in parallel with whichever phase is current once the API surface stops
moving. Likely starts mid-Phase 3.

## Cross-phase constraints (locked now, honored in every phase spec)

These constrain the API shape and cannot be deferred to "the plan will verify." They came out of
architectural review of an earlier draft; locking them as roadmap-level requirements stops the same
questions re-surfacing in each phase spec.

1. **No backward compatibility.** v2 → v3 is a clean break. The old `IControlCommandHandler`
   interface and the old `ControlCommandResult` public shape disappear. `HotRepl.Core` ships at
   3.0.0. Ardenfall and anyone else upgrading must migrate. Don't ship a parallel compat-shim
   interface — every consumer migrates within this work, so maintaining two surfaces is pure tax.

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

6. **Schema generation via NJsonSchema 10.9.0, ILRepack-internalized into `HotRepl.Core.dll`.**
   Pinning to 10.9.0 (the last release before the `System.Text.Json` transitive dependency) keeps
   the runtime surface Newtonsoft-only — known-good on Mono Unity. ILRepack-merging
   `NJsonSchema.dll` and `Namotion.Reflection.dll` into `HotRepl.Core.dll` keeps the consumer-facing
   DLL list unchanged.

7. **Plugin layout: shared-source compilation, no `Core` csproj with Unity dependency.**
   UnityCommands' command bodies live in a source folder that two loader-specific csprojs each
   `<Compile Include>` against their own `UnityEngine.dll` reference. The mod template uses the same
   layout. No `MyMod.Core` csproj with Unity dependencies — that would force a choice between Mono
   and IL2CPP-unhollowed flavors of `UnityEngine.dll`.

8. **MelonLoader bootstrap via `OnLateInitializeMelon`.** Introduced in 0.6, fires after every
   `OnInitializeMelon` across all mods — guaranteed to run after
   `GlobalControlCommandRegistry.Instance` is populated by the HotRepl host. `OnApplicationStart` is
   removed in MelonLoader 0.7.0, so don't use it.

9. **Migrations validate the design, not the design's afterthought.** AK and Erenshor in particular
   stress patterns the foundation doesn't exercise (multi-frame Unity workflows, named-artifact
   maps, Python consumer flows). Their detail specs may surface API gaps; if they do, the foundation
   spec is amended and the migration spec is rewritten against the new API. The roadmap admits this
   loop.

## Open questions (to resolve during phase-spec writing)

These can't be decided abstractly — they need the context of the specific phase that hits them.

- **AK `DataExporter.ExportAllData()` refactor for progress reporting.** Current implementation is
  synchronous, no progress hooks. To report per-exporter progress through the new typed context,
  DataExporter needs internal refactoring. The Phase 3 spec decides whether to do that refactor or
  downgrade `ak.export.run`'s progress to coarse "started / finished" events.
- **Erenshor Python consumer pattern.** `bunx @hotrepl/cli run ...` shell-out works but loses
  streaming progress unless the CLI exposes a way to consume job events. The Phase 4 spec picks:
  terminal-only with documented tradeoff vs. CLI subcommand that prints job events to stdout as they
  arrive.
- **AK game-quit mechanism.** Whether the build-tool kills the process or invokes a typed
  `ak.game.quit` command after a successful export run. Phase 3 decides based on cleanup-correctness
  vs. robustness tradeoffs.
- **Mod template `dotnet new` parameters.** Which substitutions the template manifest exposes
  (plugin GUID, author name, mod name) is a Phase 1 decision but the specifics need the template's
  actual layout in hand.

## What is explicitly NOT in scope

| Out of scope                                   | Why                                                                                                                         |
| ---------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| Hot config reload at runtime                   | Mid-flight `GlobalControlCommandRegistry` mutation while jobs are in flight is hard to get right. Restart-required is fine. |
| A second non-Unity demo plugin                 | Premature. Demonstrate Unity well first.                                                                                    |
| Publishing the template to NuGet               | GitHub-template-button covers the discoverability win. NuGet publish is a future PR.                                        |
| Publishing UnityCommands to Thunderstore       | Wrong audience — Thunderstore is for end-user game mods, UnityCommands is developer tooling.                                |
| Migrating Erenshor's `InteractiveMapCompanion` | Per maintainer call; it's a live-state streamer, not export-shaped.                                                         |
| Migrating AK's other 11 mods                   | Out of scope per maintainer call; they're gameplay tweaks, not command-shaped.                                              |
| Per-command rate limiting / permission gates   | Loopback + single-client is HotRepl's authority model. Per-command policy is YAGNI.                                         |

## Done means

- Every published HotRepl package (`@hotrepl/protocol`, `@hotrepl/sdk`, `@hotrepl/cli`,
  `@hotrepl/mcp`) is on v3 of the protocol with the new command shape.
- `HotRepl.Core` ships at 3.0.0 with NJsonSchema ILRepack-internalized.
- `HotRepl.UnityCommands` ships 4 demo commands (BepInEx + MelonLoader variants) and shows up in
  `commands_list` on every HotRepl install using bundled deployment.
- `glockyco/hotrepl-mod-template` exists, builds, deploys, and a fresh fork produces a working
  "hello world" mod within 5 minutes.
- Ardenfall, AK, and Erenshor are on the new pattern. Their existing HotRepl-driven workflows
  (`bun run hotrepl:setup`, `build-tool export`, the Python map-capture pipeline) keep working with
  no user-facing regressions.
- README, AGENTS.md, the landing page, and per-package READMEs reflect the new typed-command pattern
  as the canonical way to author HotRepl commands.
- `lefthook run pre-push --force` clean across every touched repo.
