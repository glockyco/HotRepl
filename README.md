# HotRepl

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Runtime C# REPL for Game Modding.

Agent-first runtime C# REPL that runs inside any Mono-based Unity game via BepInEx. Accepts C# code over WebSocket, compiles and executes it in the game process, and returns structured JSON responses. Built for LLM coding agents but usable by humans too.

## Features

- **WebSocket protocol** -- JSON-based request/response over a single WebSocket connection (default port 18590)
- **REPL state persistence** -- variables, types, and imports survive across evaluations within a session
- **Timeout and cancel** -- per-request timeout (default 10s) with cancel support to abort runaway code
- **Structured JSON responses** -- typed results with value, type info, stdout capture, duration, and error details
- **Main-thread dispatch** -- evaluations run on the game's main thread for safe access to Unity APIs
- **Handshake on connect** -- server advertises capabilities (C# version, default usings) on connection

## Installation (BepInEx)

1. Install [BepInEx 5.x](https://github.com/BepInEx/BepInEx) into your Unity game
2. Copy `HotRepl.Core.dll` and `HotRepl.BepInEx.dll` into `BepInEx/plugins/`
3. Launch the game
4. Connect to `ws://localhost:18590`

`Mono.CSharp.dll` is bundled as an embedded dependency in the core assembly.

## Quick Start

Connect to the WebSocket server and send JSON messages:

**Evaluate code:**
```json
{"type": "eval", "id": "1", "code": "1 + 1"}
```
```json
{"type": "eval_result", "id": "1", "hasValue": true, "value": "2", "valueType": "System.Int32", "stdout": null, "durationMs": 12}
```

**Multi-statement with state:**
```json
{"type": "eval", "id": "2", "code": "var x = 42;"}
{"type": "eval", "id": "3", "code": "x * 2"}
```

**Ping/pong heartbeat:**
```json
{"type": "ping", "id": "4"}
```
```json
{"type": "pong", "id": "4"}
```

**Reset evaluator state:**
```json
{"type": "reset", "id": "5"}
```
```json
{"type": "reset_result", "id": "5", "success": true}
```

## Protocol Reference

### Client -> Server

| Type     | Fields                          | Description                            |
|----------|---------------------------------|----------------------------------------|
| `eval`   | `id`, `code`, `timeoutMs?`      | Evaluate C# code (default timeout 10s) |
| `cancel` | `id`                            | Cancel a running evaluation by id      |
| `reset`  | `id`                            | Reset evaluator state (clear variables)|
| `ping`   | `id`                            | Heartbeat ping                         |

### Server -> Client

| Type           | Key Fields                                           | Description                     |
|----------------|------------------------------------------------------|---------------------------------|
| `handshake`    | `version`, `csharpVersion`, `defaultUsings`          | Sent on connection              |
| `eval_result`  | `id`, `hasValue`, `value`, `valueType`, `stdout`, `durationMs` | Successful evaluation  |
| `eval_error`   | `id`, `errorKind`, `message`, `stackTrace?`          | Compile error, exception, or timeout |
| `reset_result` | `id`, `success`                                      | Reset confirmation              |
| `pong`         | `id`                                                 | Heartbeat response              |

## Building from Source

```bash
dotnet build                          # Build all projects
dotnet build src/HotRepl.BepInEx/     # Build BepInEx adapter only
```

Output DLLs land in each project's `bin/Debug/netstandard2.1/` directory.

## Architecture

```
+------------------+     +--------------------+
|  HotRepl.BepInEx |---->|    HotRepl.Core    |
|  (adapter)       |     |  (engine, server,  |
|                  |     |   protocol, eval)  |
|  BepInExHost     |     |                    |
|  ReplPlugin      |     |  IReplHost --------+--- interface
+------------------+     +--------------------+
                                  |
                           Mono.CSharp.dll
                         (runtime compiler)
```

**HotRepl.Core** contains all REPL logic: the WebSocket server (Fleck), the Mono.CSharp evaluator, protocol serialization, result formatting, and the `IReplHost` abstraction. It has zero dependencies on any game framework.

**HotRepl.BepInEx** implements `IReplHost` for BepInEx 5.x on Unity Mono. It provides reference assemblies, default usings (UnityEngine, System.Linq, etc.), main-thread dispatch via `Update()`, and logging through BepInEx's `ManualLogSource`.

## Creating Custom Adapters

To run HotRepl in a different host (MelonLoader, MonoGame, standalone Mono, test harness), implement `IReplHost`:

```csharp
public interface IReplHost
{
    IReadOnlyList<Assembly> ReferenceAssemblies { get; }
    IReadOnlyList<string> DefaultUsings { get; }
    void RunOnMainThread(Action action);
    void Log(LogLevel level, string message);
}
```

Then create a `ReplEngine` and drive it:

```csharp
var host = new MyCustomHost();
var engine = new ReplEngine(host, new ReplConfig { Port = 18590 });
engine.Start();

// In your game loop:
engine.Tick();

// On shutdown:
engine.Dispose();
```

`ReplConfig` exposes: `Port`, `DefaultTimeoutMs`, `MaxResultLength`, `MaxEnumerableElements`.

## Limitations

- **C# 7.x only** -- Mono.CSharp supports up to C# 7; no `async`/`await`, no pattern matching enhancements, no nullable reference types
- **Mono runtime only** -- will not work on IL2CPP builds (the runtime compiler requires JIT)
- **Memory leaks from type definitions** -- types defined via `eval` (classes, structs) are loaded into the AppDomain and cannot be unloaded; use `reset` to mitigate but the assemblies remain in memory until the process exits
- **Single client** -- one WebSocket connection at a time; subsequent connections replace the previous one

## License

[MIT](LICENSE)
