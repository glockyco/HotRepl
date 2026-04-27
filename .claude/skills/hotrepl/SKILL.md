---
name: hotrepl
description: >
  Guide for connecting to and interacting with a HotRepl server embedded in a running
  Unity game through a supported host (BepInEx or MelonLoader). Use this skill when
  you need to evaluate C# code in a live game, inspect game objects or components,
  types and scene structure. HotRepl is a WebSocket REPL that accepts C# and returns
  structured JSON.
allowed-tools: Bash,Read,Write
version: 1.0.0
---

# HotRepl Usage Guide

HotRepl embeds a WebSocket server in a running Unity game through BepInEx or
MelonLoader. You send C# code; it executes on the game's main thread and returns
structured JSON. State persistence and timeout behavior depend on the active evaluator.

## Verify the server is running

```bash
cd /path/to/repo/client && hotrepl ping
cd /path/to/repo/client && hotrepl info
```

`pong` response means the server is up. `hotrepl info` shows the host, active evaluator,
timeout mode, and available evaluators. If the `hotrepl` CLI is unavailable, connect
with any WebSocket client (e.g. `websocat ws://localhost:18590`).
Default endpoint: `ws://localhost:18590`

## Handshake

On connection the server immediately sends:

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
    "runtime": ".NET 6",
    "platform": "Unity IL2CPP"
  },
  "availableEvaluators": ["Roslyn.Script", "Roslyn.Isolated"],
  "defaultUsings": ["System", "System.Linq", "UnityEngine", "..."],
  "helpers": ["String[] Help()", "Object Inspect(Object obj, ...)", "..."]
}
```

Read `defaultUsings` — those namespaces are already open. Read `helpers` — no
additional imports are needed; call them as `Repl.*` (core helpers), `UnityHelpers.*`,
or `Il2CppHelpers.*` when those helper assemblies are present.

## Evaluate code

Request:

```json
{"type": "eval", "id": "1", "code": "1 + 1"}
```

Success response:

```json
{"type": "eval_result", "id": "1", "hasValue": true, "value": "2", "valueType": "System.Int32", "durationMs": 8}
```

Error response:

```json
{"type": "eval_error", "id": "1", "errorKind": "compile", "message": "error CS0103: The name 'x' does not exist in the current context"}
```

`errorKind`: `compile` | `runtime` | `timeout` | `cancelled`

`id` is caller-assigned and echoed back. Use unique ids per request.

## State persists across evals

```json
{"type": "eval", "id": "2", "code": "var player = GameObject.Find(\"Player\");"}
{"type": "eval", "id": "3", "code": "player.transform.position"}
```

Variables, type definitions, and using directives survive until `reset` or reconnect when
the active evaluator reports `supportsPersistentState: true`. Use `Roslyn.Isolated` for
repeatable, stateless snippets on .NET 6 hosts.

## Common inspection patterns

### Find a GameObject

```csharp
GameObject.Find("Player")
```

### Deep-inspect a component

```csharp
Repl.Inspect(Camera.main, depth: 2)
```

### Describe a type's API

```csharp
Repl.Describe(typeof(Rigidbody))
```

### List all MonoBehaviours in the scene

```csharp
UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
    .Select(m => m.GetType().Name + " on " + m.gameObject.name)
    .ToArray()
```

### Read the full scene hierarchy (BepInEx adapter only)

```csharp
UnityHelpers.SceneGraph()
```

### Eval history

```csharp
Repl.History(limit: 10)   // returns [{code, value, error, timestamp}, ...]
```

## Watch a value over time

```json
{
  "type": "subscribe",
  "id": "watch-pos",
  "code": "GameObject.Find(\"Player\").transform.position.ToString()",
  "intervalFrames": 30,
  "onChange": true,
  "limit": 100
}
```

- `intervalFrames`: re-evaluate every N frames (default 1).
- `onChange`: only emit when the value changes from the previous tick.
- `limit`: stop after N results (0 = unlimited).
- Each tick produces `subscribe_result` or `subscribe_error`.
- `"final": true` in the response means the subscription is closed.

Cancel a subscription:

```json
{"type": "cancel", "id": "watch-pos"}
```

## Autocomplete

```json
{"type": "complete", "id": "c1", "code": "Time.", "cursorPos": 5}
```

Returns `complete_result` with `completions[]`. Does not execute code.

## Reset evaluator state

```json
{"type": "reset", "id": "r1"}
```

Clears REPL-defined variables and types for persistent evaluators. Use before starting
a fresh exploration session. Persistent evaluators may retain generated assemblies
until process exit; use `Roslyn.Isolated` for stateless snippets on .NET 6 hosts.

## Heartbeat

```json
{"type": "ping", "id": "hb1"}
```

Returns `{"type": "pong", "id": "hb1"}`. Use to verify the connection is alive before
long sequences of evals.

## Python CLI shortcuts

```bash
hotrepl info                                      # show host/evaluator metadata
hotrepl select-evaluator Roslyn.Isolated         # stateless snippets on .NET 6 hosts
hotrepl eval 'Camera.main.transform.position'    # eval and print formatted result
hotrepl eval --json 'Time.frameCount'            # raw JSON response
hotrepl complete 'Time.'                         # completions at end of snippet
hotrepl test                                     # run full protocol smoke suite
hotrepl test --url ws://192.168.1.10:18590       # against a remote game
```

The library at `client/src/hotrepl/` is importable for scripted workflows.

## Limitations — read before writing evals

| Constraint | What to do |
|---|---|
| C# version depends on evaluator | Mono.CSharp is C# 7.x; Roslyn evaluators report `latest` |
| `varName * expr` parser bug | `player * 2` is parsed as a pointer type if `player` was defined in a prior eval. Use `2 * player` instead. Affects `*` only |
| Timeout mode depends on evaluator | Mono.CSharp reports `HardAbort`; Roslyn reports `Cooperative`, so runaway runtime loops may still require restarting the game |
| One client at a time | Reconnecting replaces the session and cancels all subscriptions |
| IL2CPP requires MelonLoader host | Use `hotrepl info` to verify host/evaluator metadata before IL2CPP audits |
| Type definitions leak memory | Persistent evaluator sessions can emit assemblies that are not reclaimed until process exit; use `Roslyn.Isolated` for stateless audit snippets on .NET 6 hosts |
