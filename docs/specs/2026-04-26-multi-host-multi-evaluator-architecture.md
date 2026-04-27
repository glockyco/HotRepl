# HotRepl Multi-Host Multi-Evaluator Architecture

**Date:** 2026-04-26
**Status:** Design

## Goal

Make HotRepl a host-agnostic, evaluator-pluggable runtime C# REPL that supports both BepInEx/Mono and MelonLoader/IL2CPP Unity games without forcing either runtime into a special case.

The current shape is correct for Mono/BepInEx but does not generalize to IL2CPP. The architecture must be extended so that a single HotRepl protocol and engine serves both runtimes, Mono/BepInEx games keep working without regression, MelonLoader/IL2CPP games become first-class, and future hosts and evaluators can be added without touching core.

## Why this exists

Two real games drive this work today.

Erenshor is a BepInEx 5.x Mono Unity game. It is currently supported through `MonoCSharpEvaluator` (`mcs.dll`) and the `HotRepl.BepInEx` adapter. That support must keep working.

Ancient Kingdoms is a MelonLoader IL2CPP game whose mods are managed .NET 6 code calling generated `Il2Cpp.*` wrapper assemblies through `Il2CppInterop`. It is not supported today and cannot be supported by the existing evaluator: `Mono.CSharp.Evaluator` requires Mono JIT, which IL2CPP does not provide.

Beyond those two consumers, HotRepl should generalize so that other Mono or IL2CPP Unity games hosted by BepInEx or MelonLoader can adopt it without per-game forks.

## Scope

In scope:

- Splitting Core, Evaluators, Helpers, and Hosts into discrete assemblies with explicit boundaries.
- Adding a Roslyn evaluator (`Microsoft.CodeAnalysis.CSharp.Scripting`) with two modes: persistent script and isolated compiled snippet.
- Adding a MelonLoader host adapter.
- Extending the protocol with evaluator and host capability metadata.
- Generalizing timeout/cancellation semantics so each evaluator describes its own.
- Migrating Unity helpers out of the BepInEx adapter into a shared helper assembly.
- Adding a separate IL2CPP helper assembly for IL2CPP-capable hosts.
- Validation strategy across unit tests, BepInEx integration, and MelonLoader integration.

Out of scope:

- Embedding game-specific commands or types in HotRepl.
- Cross-process or remote evaluation.
- Compiling user code to IL2CPP.
- Unity Editor integration.

## Non-goals

- HotRepl is not a deterministic data export tool.
- HotRepl does not impose a domain model (sprites, monsters, items, etc.).
- HotRepl is not a build tool or packaging system for game data.
- HotRepl will not silently retain Mono behavior on runtimes where it is unsafe (e.g. no fake hard-abort on .NET 6).

## Architecture overview

```
HotRepl.Core                    game-agnostic, no Unity/Mono/BepInEx/MelonLoader refs
  Protocol/                     message types, error kinds, serialization
  Server/                       Fleck WebSocket server, ClientRegistry, MessageRouter
  Engine/                       ReplEngine, EvalJob, IEngineCommand, queues, watchdog
  Evaluator/                    ICodeEvaluator, EvalOutcome, CompletionResult,
                                EvaluatorCapabilities, TimeoutMode
  Helpers/                      Repl.* (Help, History, Inspect, Describe),
                                HistoryTracker, HelperInjector
  Subscriptions/                SubscriptionManager, SubscriptionState
  Serialization/                IResultSerializer, JsonResultSerializer
  IReplHost                     the only platform boundary

HotRepl.Evaluator.MonoCSharp    Mono.CSharp + mcs.dll, Mono runtimes only
HotRepl.Evaluator.Roslyn        Microsoft.CodeAnalysis.CSharp.Scripting, runtime-agnostic
                                modes: Script (persistent) and Isolated (compiled
                                snippet, optional collectible unload)

HotRepl.Helpers.Unity           UnityEngine-only helpers (SceneGraph, Screenshot,
                                Components, FindObjectsByTypeName)
HotRepl.Helpers.Il2Cpp          Il2CppInterop helpers (FindObjects, DescribeType,
                                SafeName, TryCast)

HotRepl.Host.BepInEx            BepInEx 5.x adapter, references Helpers.Unity,
                                default evaluator selectable (MonoCSharp today,
                                Roslyn after validation)
HotRepl.Host.MelonLoader        MelonLoader / .NET 6 adapter, references
                                Helpers.Unity + Helpers.Il2Cpp, default Roslyn

clients/                        Python client + CLI, protocol-only, host-agnostic
```

Game-specific code lives in consuming repos and uses HotRepl as a dependency. HotRepl never imports game-specific types or concepts.

## Boundaries and ownership

`HotRepl.Core` owns the protocol, the engine, the evaluator contract, and the serializer. It must not reference Unity, BepInEx, MelonLoader, or `Il2CppInterop`. The only platform coupling is `IReplHost`, supplied by a host adapter.

`HotRepl.Evaluator.*` projects own concrete evaluator implementations. They depend on `HotRepl.Core` for the contract and on their own compiler stack (`mcs.dll` or Roslyn). They do not reference Unity or hosts.

`HotRepl.Helpers.Unity` owns Unity-engine helpers. It depends on `UnityEngine.*` modules but no game code.

`HotRepl.Helpers.Il2Cpp` owns IL2CPP-capable helpers. It depends on `Il2CppInterop.Runtime` and `UnityEngine.CoreModule` but no game code.

`HotRepl.Host.*` projects own platform lifecycle integration. They wire `ReplEngine` into BepInEx or MelonLoader, provide logging, expose loaded assemblies and helper namespaces, and start/stop the engine. They reference helper assemblies and select a default evaluator.

The Python client targets the protocol, not any host. It must work against any HotRepl server regardless of host or evaluator.

## Evaluator contract

`ICodeEvaluator` gains capability metadata so the engine and clients can adapt without runtime sniffing.

```csharp
public sealed class EvaluatorCapabilities
{
    public string Name;                // "Mono.CSharp", "Roslyn.Script", "Roslyn.Isolated"
    public string LanguageVersion;     // "7.x", "11", "12", "preview"
    public bool SupportsPersistentState;
    public bool SupportsCompletion;
    public TimeoutMode TimeoutMode;    // HardAbort | Cooperative | None
}

public enum TimeoutMode { HardAbort, Cooperative, None }
```

Evaluators expose their capabilities through `ICodeEvaluator.Capabilities`. The engine reports them in the handshake.

Timeout semantics are evaluator-defined and honest:

- `HardAbort`: current Mono.CSharp behavior. Watchdog calls `Thread.Abort` on the main thread; evaluator catches `ThreadAbortException`, calls `ResetAbort`, and returns the abort sentinel for the engine to resolve.
- `Cooperative`: watchdog signals a `CancellationToken` plumbed through `EvalJob`. Compilation honors the token; runtime cancellation is best-effort. A hung user eval may require restarting the game. The handshake makes this explicit.
- `None`: timeout reported as best-effort wall-clock measurement; no preemption attempted.

`Reset` semantics are standardized across evaluators:

- Mono.CSharp: rebuild evaluator session (current behavior, including hot-reload assembly filtering).
- Roslyn.Script: drop `ScriptState`, retain references and imports; emitted assemblies may not be reclaimed by the runtime; this is documented.
- Roslyn.Isolated: each eval is its own collectible context; reset is a no-op except for clearing tracking and history.

`Initialize`, `Evaluate`, `Complete`, `ReferenceAssembly`, and `RunInternal` retain their current shapes but become explicitly main-thread-only and evaluator-owned. `Complete` returns empty completions for evaluators where `SupportsCompletion` is false.

## Protocol additions

All additions are backward-compatible. Existing clients keep working; new clients see more.

Handshake gains evaluator and host metadata:

```json
{
  "type": "handshake",
  "version": "1.x",
  "csharpVersion": "12",
  "evaluator": {
    "name": "Roslyn.Script",
    "languageVersion": "12",
    "supportsPersistentState": true,
    "supportsCompletion": true,
    "timeoutMode": "Cooperative"
  },
  "host": {
    "name": "MelonLoader",
    "version": "0.x",
    "runtime": ".NET 6.0",
    "platform": "Unity 6000.x IL2CPP"
  },
  "defaultUsings": [...],
  "helpers": [...]
}
```

New optional command:

```json
{ "type": "select_evaluator", "id": "...", "evaluator": "Roslyn.Isolated" }
```

A host advertises which evaluators it has loaded. The engine accepts a `select_evaluator` request only if the requested evaluator is available; otherwise it returns `select_evaluator_error` with `errorKind: "unsupported"`. The default evaluator is host-configured.

`eval_error.errorKind` gains `unsupported` for cases such as calling `complete` on an evaluator with `SupportsCompletion: false`.

`assembly_reload` (already added for Mono hot reload) remains in the protocol and is emitted only when the active evaluator reports a reload. Other evaluators never emit it.

## Threading and lifecycle

Existing invariants are preserved:

- Network (Fleck) threads only enqueue work to `ConcurrentQueue`s.
- The main thread, via `Tick()`, is the sole executor.
- Tick drain order: cancel → command queue → at most one eval → subscriptions.
- `IReplHost.Log*` methods must be thread-safe.
- All evaluator methods are main-thread-only.

New: cooperative cancellation. `EvalJob` carries a `CancellationToken`. For `Cooperative` evaluators, the watchdog cancels the token instead of aborting the thread. For `HardAbort` evaluators, the watchdog continues to abort the thread (current behavior).

Hot-reload integration (BepInEx/Mono) remains entirely inside the Mono.CSharp evaluator. Core does not know about ScriptEngine assemblies.

## Helper packaging

`UnityHelpers` moves out of `HotRepl.BepInEx` into `HotRepl.Helpers.Unity`. The current API surface is preserved:

- `UnityHelpers.SceneGraph(filter, layer, depth, maxResults)`
- `UnityHelpers.Screenshot(path)`
- `UnityHelpers.ScreenshotBase64()`
- `UnityHelpers.Components(GameObject)`
- `UnityHelpers.FindObjectsByTypeName(string)`

Initialization receives a `MonoBehaviour` for coroutine-based capture; the host wires this in.

`HotRepl.Helpers.Il2Cpp` is new and IL2CPP-only:

- `Il2CppHelpers.FindObjects(string fullTypeName)`
- `Il2CppHelpers.DescribeType(string fullTypeName)`
- `Il2CppHelpers.SafeName(object)`
- `Il2CppHelpers.TryCast(object, string fullTypeName)`

Both helper assemblies are referenced by host adapters as appropriate. They expose only their own namespaces in eval sessions; they never reference game-specific types.

## Build and deploy

Per-host bundles. No fat assembly.

BepInEx (Erenshor and other Mono games):

```
BepInEx/plugins/HotRepl/
  HotRepl.Host.BepInEx.dll
  HotRepl.Core.dll
  HotRepl.Evaluator.MonoCSharp.dll      always
  HotRepl.Evaluator.Roslyn.dll          when Roslyn validated for Mono
  HotRepl.Helpers.Unity.dll
  mcs.dll                               only when MonoCSharp shipped
  Microsoft.CodeAnalysis.*.dll          only when Roslyn shipped
  Newtonsoft.Json.dll
  Fleck.dll
```

MelonLoader (Ancient Kingdoms and other IL2CPP games):

```
Mods/
  HotRepl.Host.MelonLoader.dll
  HotRepl.Core.dll
  HotRepl.Evaluator.Roslyn.dll
  HotRepl.Helpers.Unity.dll
  HotRepl.Helpers.Il2Cpp.dll
  Microsoft.CodeAnalysis.*.dll
  Newtonsoft.Json.dll
  Fleck.dll
```

ILRepack remains optional for BepInEx (matches current `HotRepl.BepInEx.csproj`). For MelonLoader, prefer side-by-side DLLs because MelonLoader resolves dependencies from `Mods/` and `UserLibs/`. Concrete deploy layout for MelonLoader is validated during implementation against a real install.

## Persistent vs isolated Roslyn

Both Roslyn modes ship from the start.

`Roslyn.Script` (default Roslyn):

- Built on `Microsoft.CodeAnalysis.Scripting`.
- `ScriptState<object>` is carried across evals.
- Variables, types, and using directives persist within a session.
- Maps cleanly to the current Mono REPL behavior.
- Cost: emitted script assemblies accumulate over a session; `reset` clears state but does not guarantee reclamation. Documented.

`Roslyn.Isolated` (opt-in via `select_evaluator`):

- Each eval is compiled into its own assembly.
- Loaded into a collectible `AssemblyLoadContext`.
- Execution wrapped in `[MethodImpl(MethodImplOptions.NoInlining)]` to avoid root references.
- Best-effort unload after eval completes; weak-ref + `GC.Collect`/`WaitForPendingFinalizers` loop in dev mode only.
- No persistent variables across evals.
- Targeted at repeatable audit and probing scripts.

The two modes are exposed as separate `EvaluatorCapabilities.Name` values (`Roslyn.Script`, `Roslyn.Isolated`). Clients pick via `select_evaluator`.

## Migration plan

The plan must avoid regressing Erenshor.

1. Move `MonoCSharpEvaluator` out of `HotRepl.Core` into `HotRepl.Evaluator.MonoCSharp`. Behavior unchanged. Adjust references and tests.
2. Move `UnityHelpers` and `HelperInjector`'s Unity-specific wiring out of `HotRepl.BepInEx` into `HotRepl.Helpers.Unity`. The BepInEx host references the new helper assembly.
3. Add `EvaluatorCapabilities` and `TimeoutMode`. Update handshake to include `evaluator` and `host` blocks. Defaults preserve current behavior for Mono.
4. Add `HotRepl.Evaluator.Roslyn` with Script mode behind a host-side config flag; default off in BepInEx until validated.
5. Validate Roslyn under tests, then under BepInEx/Erenshor. Make Roslyn the BepInEx default only after parity is proven; keep `MonoCSharpEvaluator` available as a fallback selectable via config.
6. Add `HotRepl.Host.MelonLoader` with Roslyn default. No Mono fallback for IL2CPP runtimes.
7. Add `HotRepl.Helpers.Il2Cpp`. The MelonLoader host references it.
8. Add `Roslyn.Isolated` mode and the `select_evaluator` protocol command.
9. Update Python client to surface evaluator and host metadata, route `errorKind: "unsupported"` clearly, and tolerate hosts with different default evaluators.
10. Document timeout mode differences and how to choose an evaluator.

## Validation

Three layers, each gating the next.

Unit (no game required):

- `EvaluatorCapabilities` surface and handshake serialization.
- Roslyn.Script: eval, persistent state, references and imports, compile and runtime errors, reset.
- Roslyn.Isolated: compile + invoke + unload + leak check via `WeakReference`.
- Mono.CSharp: existing regression suite stays green.
- Protocol: handshake fields, `select_evaluator`, `select_evaluator_error`, `errorKind: "unsupported"`.

BepInEx integration (Erenshor):

- The existing Python smoke suite passes with both Mono.CSharp and Roslyn evaluators selected.
- Hot-reload behavior (assembly filtering, auto-reset, `assembly_reload` notification) preserved.
- Screenshot helper produces a PNG.
- Scene graph traversal returns expected shape.

MelonLoader integration (Ancient Kingdoms or any equivalent IL2CPP install):

- Handshake reports MelonLoader host and Roslyn evaluator.
- `1 + 1` returns `2`.
- `UnityEngine.Application.version` resolves.
- `using Il2Cpp;` and `using Il2CppInterop.Runtime;` succeed.
- `Il2CppInterop.Runtime.Il2CppType.Of<Il2Cpp.SomeKnownType>()` resolves a known game type at runtime.
- `Resources.FindObjectsOfTypeAll(...)` returns a non-empty array for an expected type.
- `Repl.Inspect` produces a structured tree for an `Il2Cpp` instance.
- `UnityHelpers.SceneGraph()` returns a populated tree.
- `UnityHelpers.Screenshot()` produces a PNG.

Promotion to "stable" requires all three layers green.

## Security and operational defaults

- The WebSocket server binds to localhost by default. Off-loopback binding requires explicit configuration.
- Single active client, current behavior; new connections replace the prior session and cancel its subscriptions.
- Optional shared-secret token in handshake; off by default, on for any non-loopback bind.
- Connection, evaluator selection, eval start, and timeout events are logged at info level.
- No telemetry. No outbound connections. No filesystem access beyond what the host provides.

## Risks

Accepted and documented:

- Roslyn dependency size: the scripting package brings several DLLs. Deploy layout must be validated under MelonLoader/CrossOver and under BepInEx Mono.
- Cooperative timeout: a runaway user eval may hang the game until the user restarts it. The handshake makes this explicit.
- Roslyn.Isolated unloading is cooperative and subject to GC. Held references (event handlers, captured locals, static caches) can prevent unload. Tests assert leak-free behavior only under controlled conditions.
- IL2CPP wrapper assembly references must be loaded by the host. If a target type's wrapper assembly is not loaded, eval fails with a clear error.
- Roslyn Mono support is plausible (the package targets `netstandard2.0`) but not confirmed until validated in Erenshor under BepInEx.

## Implementation roadmap

Phased to keep BepInEx/Mono working at every step.

1. **Refactor**: extract Mono evaluator, extract Unity helpers, formalize evaluator capabilities, update handshake. No new evaluators yet.
2. **Roslyn.Script**: add the new evaluator with unit tests outside Unity; gate behind a config flag.
3. **BepInEx Roslyn validation**: run the existing smoke suite under Roslyn in Erenshor; promote Roslyn to BepInEx default only after parity. Mono remains available.
4. **MelonLoader host**: ship `HotRepl.Host.MelonLoader` with Roslyn default; no Mono fallback.
5. **Il2Cpp helpers**: ship `HotRepl.Helpers.Il2Cpp`; reference from MelonLoader host.
6. **Roslyn.Isolated**: add the isolated mode and `select_evaluator` protocol.
7. **Client updates**: surface evaluator/host metadata, handle `select_evaluator`, present `errorKind: "unsupported"` clearly.
8. **Documentation**: update `AGENTS.md`, the HotRepl skill, and the README; document timeout-mode differences and per-host defaults.

Each phase has explicit acceptance criteria below.

## Acceptance criteria

The architecture is considered delivered when all of the following hold:

- `HotRepl.Core` has zero references to Unity, BepInEx, MelonLoader, `Il2CppInterop`, or any game type.
- `HotRepl.Evaluator.MonoCSharp` and `HotRepl.Evaluator.Roslyn` are independently buildable; either can be omitted from a deploy without breaking the other.
- `HotRepl.Host.BepInEx` and `HotRepl.Host.MelonLoader` each ship and run their default evaluator, and at least one shared evaluator (Roslyn) works on both.
- The handshake reports evaluator and host metadata, and the `select_evaluator` command is honored when the requested evaluator is available.
- The Python client surfaces evaluator and host metadata to users.
- Existing BepInEx/Erenshor workflows pass without regression on Mono.CSharp; Roslyn passes the same smoke suite when selected.
- MelonLoader/Ancient Kingdoms (or equivalent IL2CPP target) passes the integration checks listed under Validation.
- Documentation explains timeout modes, evaluator selection, and per-host defaults.
