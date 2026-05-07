# HotRepl

Runtime C# REPL over WebSocket for Unity games through BepInEx/Mono or
MelonLoader/IL2CPP. Accepts C# code, compiles and executes it in the game
process on the main thread, returns structured JSON.
Primary audience: coding agents. License: MIT.

## Connect

Default endpoint: `ws://localhost:18590`

On connection the server immediately sends a `handshake` message — read it before
sending evals. It lists evaluator/host metadata, opened namespaces, and available helpers.

```json
{
  "type": "handshake",
  "version": "1.0.0",
  "csharpVersion": "latest",
  "evaluator": {
    "name": "Roslyn.Script",
    "languageVersion": "latest",
    "supportsPersistentState": true,
    "supportsCompletion": false,
    "timeoutMode": "Cooperative"
  },
  "host": {
    "name": "MelonLoader",
    "version": "0.x",
    "runtime": ".NET 6",
    "platform": "Unity IL2CPP"
  },
  "availableEvaluators": ["Roslyn.Script", "Roslyn.Isolated"],
  "defaultUsings": ["System", "System.Linq", "HotRepl.Helpers.Unity"],
  "helpers": ["String[] Help()", "Object History(Int32 limit = 20)"]
}
```

Eval REPL access remains single-client per server — a new connection replaces the previous
session and cancels all active subscriptions. The typed control plane adds optional
auth and exclusive leases for automation clients.

## Protocol

All messages are UTF-8 JSON with a `type` discriminant; `id` is caller-assigned and
echoed verbatim. See [`src/HotRepl.Core/Protocol/Messages.cs`](src/HotRepl.Core/Protocol/Messages.cs) for the full schema.

The additive control plane supports typed commands, cooperative jobs, artifact
references, auth, and exclusive controller leases while preserving the eval
protocol. See [`docs/control-plane-protocol.md`](docs/control-plane-protocol.md).

Notable behaviors:
- `final: true` on a `subscribe_result` or `subscribe_error` means the subscription
  is now closed (limit reached, unrecoverable error, or reset)
- `errorKind`: `compile` | `runtime` | `timeout` | `cancelled` | `unsupported`

## Evaluation Semantics

- **Persistent state**: variables, using directives, and type definitions survive
  across evals within a session. Use `reset` to clear them.
- **Main thread**: all evals run on the game's main thread. At most one executes per
  frame; the rest queue.
- **Timeout**: wall-clock budget per eval (default 10 s), overridable via `timeoutMs`.
  Enforcement depends on the active evaluator: Mono.CSharp reports `HardAbort`;
  Roslyn reports `Cooperative`.
- **C# version**: depends on the active evaluator. Mono.CSharp is C# 7.x;
  Roslyn evaluators report `latest`.

## Built-in Helpers

Injected as the static class `Repl`. Call `Repl.Help()` for the current full list.

| Method | Returns | Description |
|---|---|---|
| `Repl.Help()` | `string[]` | Signatures of all available helpers |
| `Repl.History(int limit=20)` | `object[]` | Recent evals: `{code, value, error, timestamp}` |
| `Repl.Inspect(object obj, int depth=2, int maxChildren=50)` | `object` | Deep reflection dictionary; handles circular refs |
| `Repl.Describe(Type type)` | `object` | Type metadata: base, interfaces, properties, fields, methods |

Hosts can inject additional helpers (e.g. `UnityHelpers.SceneGraph()`,
`UnityHelpers.Screenshot()`, and `Il2CppHelpers.FindObjects()` on IL2CPP hosts).
They appear in `handshake.helpers[]`.

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

### Common

```bash
dotnet build src/HotRepl.Core/        # Core only; no Unity DLLs needed
dotnet build src/HotRepl.BepInEx/     # Requires Unity DLLs in lib/
dotnet test tests/HotRepl.Tests/      # Unit tests; no game required
```

### BepInEx / Mono

Output: `src/<Project>/bin/Debug/netstandard2.1/`. Deploy to BepInEx:

```bash
GAME_DIR="/path/to/game"
cp -f src/HotRepl.BepInEx/bin/Debug/netstandard2.1/HotRepl.BepInEx.dll "$GAME_DIR/BepInEx/plugins/"
cp -f lib/mcs.dll "$GAME_DIR/BepInEx/plugins/"
```

### MelonLoader / IL2CPP

Build with game-provided paths:

```bash
dotnet build src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj \
  -p:MelonLoaderPath="/path/to/Game/MelonLoader" \
  -p:Il2CppAssembliesPath="/path/to/Game/MelonLoader/Il2CppAssemblies"
```

Deploy the host, Core, Roslyn evaluator, Unity helpers, Fleck, Newtonsoft.Json,
and Roslyn DLLs side-by-side in the game's `Mods/` directory. Do not deploy
`System.*` framework sidecars unless a game-specific deploy guide has validated
that resolver layout. The MelonLoader host uses Roslyn.Script by default and reports
`timeoutMode: "Cooperative"` in the handshake.

After deploying to an IL2CPP game, verify:

```bash
hotrepl info
hotrepl eval '1 + 1'
hotrepl eval 'UnityEngine.Application.version'
hotrepl eval 'Il2CppHelpers.DescribeType("Il2Cpp.Monster")'    # use a type present in the target game
hotrepl eval 'Il2CppHelpers.FindObjects("Il2Cpp.Monster").Length'
hotrepl control describe
hotrepl control call archive.preflight '{}'
```

Use a game-local wrapper type for the last two commands; HotRepl itself remains
game-agnostic.

## Known Limitations

| Limitation | Details |
|---|---|
| Timeout mode depends on evaluator | Mono.CSharp reports `HardAbort`; Roslyn reports `Cooperative`, so a runaway runtime loop can still require restarting the game |
| Completion depends on evaluator | Mono.CSharp supports completion; Roslyn evaluators currently report `supportsCompletion: false` |
| Type memory leak | Persistent evaluator sessions can emit assemblies that are not reclaimed until process exit; use `Roslyn.Isolated` for stateless audit snippets on .NET 6 hosts |
| Single client | A new WebSocket connection replaces the prior session; old subscriptions are cancelled |
