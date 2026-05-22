# HotRepl

HotRepl is a runtime C# REPL and typed command bridge for Unity games. It runs inside a game through
BepInEx/Mono or MelonLoader/IL2CPP, executes work on Unity's main thread, and exposes a v2 WebSocket
protocol for coding agents, CLI automation, and MCP tools.

## Requirements

- .NET 10.x for the Core test project.
- Bun 1.3.14 for the TypeScript protocol, SDK, CLI, MCP, testing, and conformance packages.
- Unity/BepInEx or MelonLoader assemblies only when building host adapters.

## Quickstart

```bash
dotnet build src/HotRepl.Core/ --nologo -v q
dotnet test tests/HotRepl.Tests/ --nologo -v q
bun install --frozen-lockfile
bun run test
bun run typecheck
```

A running host listens on `ws://127.0.0.1:18590` by default. The TypeScript SDK also uses that URL
unless `HOTREPL_URL` or an explicit URL is supplied.

```bash
bun packages/cli/src/index.ts info
bun packages/cli/src/index.ts eval '1 + 1'
bun packages/cli/src/index.ts run archive.preflight '{}'
bun packages/mcp/src/index.ts
```

## Protocol v2

The server sends a `handshake` immediately after a WebSocket connection opens:

```json
{
  "type": "handshake",
  "protocolVersion": 2,
  "host": { "name": "MelonLoader", "version": "0.x", "platform": "Unity IL2CPP" },
  "evaluator": {
    "name": "Roslyn.Script",
    "languageVersion": "latest",
    "persistentState": true,
    "supportsCompletion": false,
    "cancellation": "cooperative"
  },
  "control": { "supported": true, "commandsListChanged": false, "schemaValidation": true },
  "limits": {
    "maxMessageBytes": 4194304,
    "maxQueuedCommands": 32,
    "maxResultLength": 102400,
    "maxEnumerableElements": 100,
    "defaultEvalTimeoutMs": 10000,
    "maxJobConcurrency": 1
  },
  "enforces": ["maxMessageBytes", "maxQueuedCommands", "maxResultLength"]
}
```

All frames are UTF-8 JSON with a `type` discriminant and caller-assigned `id` where a response is
expected. Runtime errors use a single `error` envelope with closed `kind` values. Typed commands use
`commands_list`, `command_describe`, and `command_call`; job commands return `job_accepted`, then
`job_status` returns either running state or the terminal `job_result`.

See [`docs/control-plane-protocol.md`](docs/control-plane-protocol.md) for the message inventory.

## TypeScript packages

| Package                | Purpose                                                             |
| ---------------------- | ------------------------------------------------------------------- |
| `@hotrepl/protocol`    | v2 constants, TypeBox schemas, and message types                    |
| `@hotrepl/sdk`         | `connect`, `Session`, `Artifact`, typed errors, WebSocket transport |
| `@hotrepl/testing`     | `FakeRuntime`, `MockSession`, recorder, and replay helpers          |
| `@hotrepl/conformance` | Protocol conformance suite for FakeRuntime and optional real hosts  |
| `@hotrepl/cli`         | `hotrepl` command-line adapter over the SDK                         |
| `@hotrepl/mcp`         | fixed nine-tool MCP stdio server over the SDK                       |

## Evaluation semantics

- Persistent evaluator state survives across evals until `reset`; generated types may remain loaded
  until process exit.
- Fleck callbacks enqueue work only. The main-thread `Tick()` path is the sole executor.
- Tick drain order is fixed: cancels, command queue, at most one eval, subscriptions.
- A new WebSocket connection replaces the previous client and emits `session_evicted`.
- Mono.CSharp evaluates C# 7.x; Roslyn evaluators report `latest`.
- Timeout enforcement is capability-driven: `hardAbort`, `cooperative`, or `unsupported`.

## Embedding

Implement [`IReplHost`](src/HotRepl.Core/IReplHost.cs) and drive `ReplEngine` from the host's main
thread:

```csharp
var engine = new ReplEngine(new MyHost());
engine.Start();

// per frame
engine.Tick();

engine.Dispose();
```

`IReplHost` is the only platform boundary. Core stays free of BepInEx, UnityEngine, MelonLoader,
Il2CppInterop, game-specific types, `mcs.dll`, and Roslyn packages.

## Building host adapters

```bash
dotnet build src/HotRepl.BepInEx/ --nologo -v q

dotnet build src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj \
  -p:MelonLoaderPath="/path/to/Game/MelonLoader" \
  -p:Il2CppAssembliesPath="/path/to/Game/MelonLoader/Il2CppAssemblies"
```

BepInEx deploys `HotRepl.BepInEx.dll`, `HotRepl.Core.dll`, Fleck, Newtonsoft.Json, and `mcs.dll`
side-by-side. MelonLoader deploys the host, Core, Roslyn evaluator, Unity helpers, Fleck,
Newtonsoft.Json, and Roslyn dependencies in `Mods/`.

## Contributing

```bash
brew install lefthook dprint actionlint commitlint typos
bun install --frozen-lockfile
dotnet tool restore
lefthook install
lefthook run pre-push --force
```

`pre-push` mirrors CI: Bun tests/typecheck/schema export, dprint, typos, actionlint, C# build, and
C# tests. See [`AGENTS.md`](AGENTS.md) for agent-specific constraints and targeted commands.

## License

MIT
