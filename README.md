# HotRepl

Runtime C# REPL over WebSocket for Mono-based games. Accepts C# code, compiles and
executes it in the game process on the main thread, returns structured JSON.
Primary audience: coding agents. License: MIT.

## Connect

Default endpoint: `ws://localhost:18590`

On connection the server immediately sends a `handshake` message — read it before
sending evals. It lists the C# version, opened namespaces, and available helpers.

```json
{
  "type": "handshake",
  "version": "1.0.0",
  "csharpVersion": "7.x",
  "defaultUsings": ["System", "System.Linq", "UnityEngine", "..."],
  "helpers": ["String[] Help()", "Object History(Int32 limit = 20)", "..."]
}
```

No authentication. Single client per server — a new connection replaces the previous
session and cancels all active subscriptions.

## Protocol

All messages are UTF-8 JSON objects with a `type` discriminant string. `id` is
caller-assigned (any non-empty string) and echoed verbatim in the response.

### Client → Server

| `type`      | Required fields       | Optional fields                                                                       | Description                                      |
|-------------|----------------------|---------------------------------------------------------------------------------------|--------------------------------------------------|
| `eval`      | `id`, `code`         | `timeoutMs` (int, default: 10000)                                                     | Evaluate C# expression or statement(s)           |
| `cancel`    | `id`                 | —                                                                                     | Cancel a running or queued eval by id            |
| `reset`     | `id`                 | —                                                                                     | Clear all variables and type definitions         |
| `ping`      | `id`                 | —                                                                                     | Heartbeat                                        |
| `complete`  | `id`, `code`         | `cursorPos` (int, default: -1 meaning end-of-string)                                  | Autocomplete suggestions; does not execute code  |
| `subscribe` | `id`, `code`         | `intervalFrames` (int, default: 1), `onChange` (bool, default: false), `limit` (int, 0 = unlimited), `timeoutMs` (int) | Repeated evaluation on a frame timer or value change |

### Server → Client

| `type`             | Key fields                                                               | Emitted when                        |
|--------------------|--------------------------------------------------------------------------|-------------------------------------|
| `handshake`        | `version`, `csharpVersion`, `defaultUsings[]`, `helpers[]`              | Client connects                     |
| `eval_result`      | `id`, `hasValue` (bool), `value?` (string), `valueType?`, `stdout?`, `durationMs` (ms) | Eval succeeded             |
| `eval_error`       | `id`, `errorKind`, `message`, `stackTrace?`                             | Eval failed                         |
| `reset_result`     | `id`, `success` (bool)                                                   | Reset complete                      |
| `pong`             | `id`                                                                     | Ping received                       |
| `complete_result`  | `id`, `completions[]`, `durationMs`                                      | Autocomplete done                   |
| `subscribe_result` | `id`, `seq` (int), `hasValue`, `value?`, `valueType?`, `durationMs`, `final` (bool) | Subscription tick          |
| `subscribe_error`  | `id`, `seq`, `errorKind`, `message`, `final`                            | Subscription tick failed            |

`errorKind` values: `compile` | `runtime` | `timeout` | `cancelled`

`final: true` on a subscribe message means the subscription is now closed (limit
reached, unrecoverable error, or reset).

## Evaluation Semantics

- **Persistent state**: variables, using directives, and type definitions survive
  across evals within a session. Use `reset` to clear them.
- **Main thread execution**: all evals run on the game's main thread (Unity
  `Update()` loop). At most one eval executes per frame. Queued evals are processed
  in order.
- **Timeout**: wall-clock budget per eval (default 10 s). A watchdog fires
  `Thread.Abort` on the main thread and returns `eval_error` with
  `errorKind: "timeout"`. Override per-request with `timeoutMs`.
- **C# 7.x only**: `async`/`await`, nullable reference types, and C# 8+ pattern
  matching are not supported. The Mono.CSharp evaluator is pinned to C# 7.
- **`varName * expr` parser bug**: when `varName` was previously defined in a REPL
  session, Mono's interactive parser reads `varName * 2` as a pointer-type
  declaration, not multiplication. Use `2 * varName` (literal on left) or a method
  call. Operators `+`, `-`, and `/` are not affected.

## Built-in Helpers

Injected into every session as the static class `Repl`. Call `Repl.Help()` after
connecting for the current full signature list.

| Method | Returns | Description |
|---|---|---|
| `Repl.Help()` | `string[]` | Signatures of all available helpers |
| `Repl.History(int limit=20)` | `object[]` | Recent evals: `{code, value, error, timestamp}` |
| `Repl.Inspect(object obj, int depth=2, int maxChildren=50)` | `object` | Deep reflection dictionary; handles circular refs |
| `Repl.Describe(Type type)` | `object` | Type metadata: base, interfaces, properties, fields, methods |

BepInEx adapter injects additional Unity helpers (e.g. `UnityHelpers.SceneGraph()`,
`UnityHelpers.Screenshot()`). They appear in `handshake.helpers[]`.

## Architecture

```
HotRepl.slnx
src/
  HotRepl.Core/               # Framework-agnostic; netstandard2.1; no game dependencies
    IReplHost.cs              # Host contract: Config, Log*, AdditionalAssemblies/Usings/Helpers
    ReplEngine.cs             # Composition root; drives all subsystems; called by host's Update()
    ReplConfig.cs             # Port, DefaultTimeoutMs, MaxResultLength, MaxEnumerableElements
    Evaluator/                # MonoEvaluator wraps Mono.CSharp; ICodeEvaluator + EvalOutcome
    Protocol/                 # Messages.cs (wire records), MessageSerializer.cs
    Helpers/                  # Repl.cs (user-facing helpers), HelperInjector.cs
    Serialization/            # JsonResultSerializer (value → truncated JSON string)
    Server/                   # ReplWebSocketServer (Fleck), ClientRegistry, MessageRouter
    Subscriptions/            # SubscriptionManager, SubscriptionJob
  HotRepl.BepInEx/            # BepInEx 5.x adapter; netstandard2.1; requires Unity DLLs in lib/
    ReplPlugin.cs             # Plugin entry point; Awake() → Start(), Update() → Tick()
    BepInExHost.cs            # IReplHost: Unity assemblies, usings, BepInEx logging
lib/
  mcs.dll                     # Mono compiler (mcs-unity fork); ships alongside plugin
tests/
  HotRepl.Tests/              # xUnit; net10.0; no game required
client/                       # Python reference client + protocol smoke tests
  src/hotrepl/                # hotrepl CLI + async WebSocket client library
  tests/                      # ~38 pytest tests covering the full protocol surface
```

**Threading model**: Fleck threads write to `ConcurrentQueue`s. `Tick()` drains them
on the main thread. The watchdog timer runs on a pool thread and calls `Thread.Abort`
on the main thread reference captured at `Start()`.

**Tick drain order** (invariant): (1) cancel requests, (2) command queue (reset/ping/
complete/subscribe), (3) at most one eval, (4) subscription ticks.

## Creating Adapters

To embed HotRepl in a host other than BepInEx (MelonLoader, MonoGame, standalone
Mono, test harness), implement `IReplHost` and drive `ReplEngine`:

```csharp
public interface IReplHost
{
    ReplConfig Config { get; }
    void LogInfo(string message);
    void LogDebug(string message);
    void LogWarning(string message);
    void LogError(string message, Exception? ex = null);
    IReadOnlyList<Assembly> AdditionalAssemblies { get; }  // extra reference assemblies
    IReadOnlyList<string> AdditionalUsings { get; }        // extra opened namespaces
    string[] AdditionalHelperSignatures { get; }           // merged into handshake.helpers[]
}
```

```csharp
var engine = new ReplEngine(new MyHost());
engine.Start();   // call once from the main thread; starts WebSocket server

// per-frame (game loop):
engine.Tick();    // drains queues; executes at most one eval; ticks subscriptions

// on shutdown:
engine.Dispose();
```

## Configuration

All properties have safe defaults; only override what you need.

| Property | Default | Description |
|---|---|---|
| `Port` | `18590` | WebSocket listen port |
| `DefaultTimeoutMs` | `10000` | Per-eval wall-clock budget (ms); overridable per-request |
| `MaxResultLength` | `100000` | Max characters in a serialized result before truncation |
| `MaxEnumerableElements` | `100` | Max items enumerated from a collection result |

## Building

```bash
dotnet build src/HotRepl.Core/        # Core only; no Unity DLLs needed
dotnet build src/HotRepl.BepInEx/     # Requires Unity DLLs in lib/
dotnet test tests/HotRepl.Tests/      # Unit tests; no game required
```

Output lands in `src/<Project>/bin/Debug/netstandard2.1/`.

Deploy to BepInEx:

```bash
GAME_DIR="/path/to/game"
cp -f src/HotRepl.BepInEx/bin/Debug/netstandard2.1/HotRepl.BepInEx.dll "$GAME_DIR/BepInEx/plugins/"
cp -f lib/mcs.dll "$GAME_DIR/BepInEx/plugins/"
```

## Known Limitations

| Limitation | Details |
|---|---|
| C# 7.x only | Mono.CSharp supports C# 7; `async`/`await`, nullable reference types, and C# 8+ features are unavailable |
| Mono JIT only | Will not work on IL2CPP Unity builds; the runtime compiler requires JIT |
| Type memory leak | Classes/structs/enums defined via eval are loaded into AppDomain assemblies that cannot be unloaded; `reset` does not free them |
| Single client | A new WebSocket connection replaces the prior session; old subscriptions are cancelled |
| `varName * expr` | When `varName` was defined in a prior eval, Mono's parser reads this as a pointer-type declaration. Use `2 * varName`. Affects `*` only |
| `Thread.Abort` unreliable in tight loops | Mono does not inject safepoints at loop back-edges; `while(true){}` may not abort on timeout. Restart the game process to recover |
