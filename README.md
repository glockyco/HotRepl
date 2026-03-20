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

All messages are UTF-8 JSON with a `type` discriminant; `id` is caller-assigned and
echoed verbatim. See [`src/HotRepl.Core/Protocol/Messages.cs`](src/HotRepl.Core/Protocol/Messages.cs) for the full schema.

Notable behaviors:
- `final: true` on a `subscribe_result` or `subscribe_error` means the subscription
  is now closed (limit reached, unrecoverable error, or reset)
- `errorKind`: `compile` | `runtime` | `timeout` | `cancelled`

## Evaluation Semantics

- **Persistent state**: variables, using directives, and type definitions survive
  across evals within a session. Use `reset` to clear them.
- **Main thread**: all evals run on the game's main thread. At most one executes per
  frame; the rest queue.
- **Timeout**: wall-clock budget per eval (default 10 s), overridable via `timeoutMs`.
  On expiry a watchdog fires `Thread.Abort` and returns `errorKind: "timeout"`.
- **C# 7.x only**: `async`/`await`, nullable reference types, and C# 8+ features are
  not supported.

## Built-in Helpers

Injected as the static class `Repl`. Call `Repl.Help()` for the current full list.

| Method | Returns | Description |
|---|---|---|
| `Repl.Help()` | `string[]` | Signatures of all available helpers |
| `Repl.History(int limit=20)` | `object[]` | Recent evals: `{code, value, error, timestamp}` |
| `Repl.Inspect(object obj, int depth=2, int maxChildren=50)` | `object` | Deep reflection dictionary; handles circular refs |
| `Repl.Describe(Type type)` | `object` | Type metadata: base, interfaces, properties, fields, methods |

The BepInEx adapter injects additional Unity helpers (e.g. `UnityHelpers.SceneGraph()`,
`UnityHelpers.Screenshot()`). They appear in `handshake.helpers[]`.

## Embedding

Implement [`IReplHost`](src/HotRepl.Core/IReplHost.cs) and drive `ReplEngine`:

```csharp
var engine = new ReplEngine(new MyHost());
engine.Start();   // once, from the main thread

// per-frame:
engine.Tick();

// on shutdown:
engine.Dispose();
```

`IReplHost` is the only coupling point between `HotRepl.Core` and any platform.
It supplies extra assemblies, opened namespaces, and helper signatures for the
handshake. See [`ReplConfig.cs`](src/HotRepl.Core/ReplConfig.cs) for configuration
options (all properties have safe defaults and XML doc comments).

## Building

```bash
dotnet build src/HotRepl.Core/        # Core only; no Unity DLLs needed
dotnet build src/HotRepl.BepInEx/     # Requires Unity DLLs in lib/
dotnet test tests/HotRepl.Tests/      # Unit tests; no game required
```

Output: `src/<Project>/bin/Debug/netstandard2.1/`. Deploy to BepInEx:

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