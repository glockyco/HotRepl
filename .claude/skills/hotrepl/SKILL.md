---
name: hotrepl
description: >
  Guide for connecting to and interacting with a HotRepl server embedded in a running
  Unity game through a supported host (BepInEx or MelonLoader). Use this skill when
  you need to evaluate C# code in a live game, inspect game objects or components,
  types and scene structure. HotRepl is a WebSocket REPL that accepts C# and returns
  structured JSON.
allowed-tools: Bash,Read,Write
version: 2.0.0
---

# HotRepl Usage Guide

HotRepl embeds a WebSocket server in a running Unity game through BepInEx or MelonLoader. You send
C# code or typed command calls; work executes on the game's main thread and returns structured JSON.
State persistence, completion, and timeout behavior are reported by the handshake.

## Verify the server is running

```bash
hotrepl info
hotrepl eval '1 + 1'
```

Default endpoint: `ws://127.0.0.1:18590`. Override with `HOTREPL_URL` or CLI `--url`. If the CLI is
unavailable, connect with any WebSocket client and read the initial `handshake` frame before sending
requests.

## Handshake

On connection the server immediately sends:

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
  "availableEvaluators": ["Roslyn.Script", "Roslyn.Isolated"],
  "defaultUsings": ["System", "System.Linq", "UnityEngine"],
  "helpers": ["String[] Help()", "Object Inspect(Object obj, ...)", "..."],
  "control": { "supported": true, "commandsListChanged": false, "schemaValidation": false },
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

Read `defaultUsings` and `helpers` before writing imports. Helpers appear as `Repl.*`,
`UnityHelpers.*`, or `Il2CppHelpers.*` when those assemblies are present.

## Evaluate code

```json
{ "type": "eval", "id": "eval-1", "code": "1 + 1" }
```

Success:

```json
{
  "type": "eval_result",
  "id": "eval-1",
  "hasValue": true,
  "value": "2",
  "valueType": "System.Int32",
  "durationMs": 8
}
```

Failure:

```json
{
  "type": "eval_error",
  "id": "eval-1",
  "error": {
    "kind": "validation_failed",
    "code": "compileError",
    "message": "error CS0103: The name 'x' does not exist in the current context",
    "retryable": false
  }
}
```

Closed error kinds: `validation_failed`, `precondition_failed`, `conflict`, `timeout`, `cancelled`,
`busy`, `unknown_command`, `unsupported_operation`, `artifact_missing`, `invalid_request`,
`internal`.

## State persists across evals

```json
{ "type": "eval", "id": "eval-2", "code": "var player = GameObject.Find(\"Player\");" }
{ "type": "eval", "id": "eval-3", "code": "player.transform.position" }
```

Variables, type definitions, and using directives survive until `reset` or reconnect when the active
evaluator reports `persistentState: true`. Use `Roslyn.Isolated` for repeatable, stateless snippets
on .NET 6 hosts.

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

### Read the full scene hierarchy

```csharp
UnityHelpers.SceneGraph()
// Available when the host injects Unity helpers.
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
  "limit": 100
}
```

Each tick produces `subscribe_result` or `subscribe_error`. `final: true` means the subscription is
closed.

## Typed commands

List and describe commands before calling them:

```json
{ "type": "commands_list", "id": "commands-1" }
{ "type": "command_describe", "id": "describe-1", "name": "archive.preflight" }
```

Run a sync command:

```json
{ "type": "command_call", "id": "cmd-1", "name": "archive.preflight", "args": {} }
```

Run a job command: `command_call` returns `job_accepted`; poll `job_status` until it returns
terminal `job_result`. Clients do not send a separate `job_result` request in v2.

## Artifact references

Command outputs include named artifact references instead of bulk payloads. Verify `sha256`, size,
`finalized`, schema, and counts before trusting artifact content. The TypeScript SDK `Artifact`
helper rehashes bytes before returning `bytes()`, `text()`, `json()`, or `open()` metadata.

## TypeScript CLI shortcuts

```bash
hotrepl info                                      # show host/evaluator metadata
hotrepl eval 'Camera.main.transform.position'    # eval and print formatted result
hotrepl eval 'Time.frameCount' --format json     # JSON output
hotrepl complete 'Time.'                         # completions at end of snippet
hotrepl run archive.preflight '{}'               # typed command call
hotrepl describe archive.preflight               # descriptor details
hotrepl journal --limit 20                       # recent eval/command entries
```

## Limitations — read before writing evals

| Constraint                        | What to do                                                                                                                                                |
| --------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| C# version depends on evaluator   | Mono.CSharp is C# 7.x; Roslyn evaluators report `latest`                                                                                                  |
| `varName * expr` parser bug       | `player * 2` is parsed as a pointer type if `player` was defined in a prior eval. Use `2 * player` instead. Affects `*` only                              |
| Timeout mode depends on evaluator | Mono.CSharp reports `hardAbort`; Roslyn reports `cooperative`, so runaway runtime loops may still require restarting the game                             |
| One client at a time              | Reconnecting replaces the session and cancels all subscriptions                                                                                           |
| IL2CPP requires MelonLoader host  | Use `hotrepl info` to verify host/evaluator metadata before IL2CPP audits                                                                                 |
| Type definitions leak memory      | Persistent evaluator sessions can emit assemblies that are not reclaimed until process exit; use `Roslyn.Isolated` for stateless snippets on .NET 6 hosts |
