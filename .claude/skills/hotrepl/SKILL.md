---
name: hotrepl
description: >
  Guide for connecting to and interacting with a HotRepl server embedded in a running
  Mono/Unity game. Use this skill when you need to evaluate C# code in a live game,
  inspect game objects or components, watch values change over time, or explore Unity
  types and scene structure. HotRepl is a WebSocket REPL that accepts C# and returns
  structured JSON.
allowed-tools: Bash,Read,Write
version: 1.0.0
---

# HotRepl Usage Guide

HotRepl embeds a WebSocket server in a running Mono/Unity game (via BepInEx). You send
C# code; it executes on the game's main thread and returns structured JSON. State is
persistent across requests within a session.

## Verify the server is running

```bash
cd /path/to/repo/client && hotrepl ping
```

`pong` response means the server is up. If the `hotrepl` CLI is unavailable, connect
with any WebSocket client (e.g. `websocat ws://localhost:18590`).

Default endpoint: `ws://localhost:18590`

## Handshake

On connection the server immediately sends:

```json
{
  "type": "handshake",
  "version": "1.0.0",
  "csharpVersion": "7.x",
  "defaultUsings": ["System", "System.Linq", "UnityEngine", "..."],
  "helpers": ["String[] Help()", "Object Inspect(Object obj, ...)", "..."]
}
```

Read `defaultUsings` — those namespaces are already open. Read `helpers` — no
additional imports are needed; call them as `Repl.*` (core helpers) or
`UnityHelpers.*` (BepInEx Unity helpers).

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

Variables, type definitions, and using directives survive until `reset` or reconnect.

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

Clears all REPL-defined variables and types. Use before starting a fresh exploration
session. Note: AppDomain memory from previously defined types is not freed (Mono
limitation) — restart the game to fully clean up.

## Heartbeat

```json
{"type": "ping", "id": "hb1"}
```

Returns `{"type": "pong", "id": "hb1"}`. Use to verify the connection is alive before
long sequences of evals.

## Python CLI shortcuts

```bash
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
| C# 7.x only | No `async`/`await`, no records, no nullable reference types, no C# 8+ pattern matching |
| `varName * expr` parser bug | `player * 2` is parsed as a pointer type if `player` was defined in a prior eval. Use `2 * player` instead. Affects `*` only |
| `Thread.Abort` unreliable in tight loops | `while(true){}` may not abort on timeout; if the game hangs, restart it |
| One client at a time | Reconnecting replaces the session and cancels all subscriptions |
| IL2CPP builds unsupported | HotRepl requires Mono JIT; it will not work in IL2CPP Unity builds |
| Type definitions leak memory | Classes/structs defined via eval cannot be unloaded; use `reset` to clear variables, restart the game to free memory |
