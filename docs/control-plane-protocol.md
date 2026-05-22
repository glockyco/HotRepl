# HotRepl v2 Protocol

HotRepl v2 uses one WebSocket JSON protocol for eval, subscriptions, typed commands, jobs,
artifacts, and journal queries. There is no v1 auth, lease, ping, profile, or Python-client
compatibility surface.

## Handshake

The server sends `handshake` immediately after connection:

```json
{
  "type": "handshake",
  "protocolVersion": 2,
  "host": { "name": "BepInEx", "version": "0.x", "platform": "Unity Mono" },
  "evaluator": {
    "name": "Mono.CSharp",
    "languageVersion": "7.x",
    "persistentState": true,
    "supportsCompletion": true,
    "cancellation": "hardAbort"
  },
  "availableEvaluators": ["Mono.CSharp"],
  "defaultUsings": ["System"],
  "helpers": ["String[] Help()"],
  "control": { "supported": true, "commandsListChanged": false, "schemaValidation": true },
  "limits": {
    "maxMessageBytes": 4194304,
    "maxQueuedCommands": 32,
    "maxResultLength": 102400,
    "maxEnumerableElements": 100,
    "defaultEvalTimeoutMs": 10000,
    "maxJobConcurrency": 1
  },
  "enforces": [
    "maxMessageBytes",
    "maxQueuedCommands",
    "maxResultLength",
    "maxEnumerableElements",
    "maxJobConcurrency"
  ]
}
```

`control.supported` means typed commands are available. Mutating commands do not require leases in
v2; loopback/single-client authority is the trust boundary.

## Error envelope

Failures use one envelope:

```json
{
  "kind": "validation_failed",
  "code": "schemaValidationFailed",
  "message": "Input did not match the command schema.",
  "retryable": false,
  "details": { "path": "/scene" }
}
```

Closed `kind` values are `validation_failed`, `precondition_failed`, `conflict`, `timeout`,
`cancelled`, `busy`, `unknown_command`, `unsupported_operation`, `artifact_missing`,
`invalid_request`, and `internal`.

## Eval and reset

```json
{ "type": "eval", "id": "eval-1", "code": "1 + 1", "timeoutMs": 10000 }
```

Success returns `eval_result`; failure returns `eval_error` with `error`:

```json
{ "type": "eval_result", "id": "eval-1", "hasValue": true, "value": "2", "durationMs": 3 }
{ "type": "eval_error", "id": "eval-1", "error": { "kind": "internal", "code": "runtimeException", "message": "...", "retryable": false } }
```

`reset` clears persistent evaluator variables and returns `reset_result`.

## Subscriptions

```json
{
  "type": "subscribe",
  "id": "watch-1",
  "code": "Time.frameCount",
  "intervalFrames": 1,
  "limit": 10
}
```

The server emits `subscribe_result` frames until `final: true`, or `subscribe_error` with `error`. A
new client connection sends `session_evicted` to the old session and closes its subscriptions.

## Typed commands

List commands:

```json
{ "type": "commands_list", "id": "list-1" }
```

Describe one command:

```json
{ "type": "command_describe", "id": "describe-1", "name": "archive.preflight" }
```

Descriptors include `name`, `majorVersion`, `kind` (`sync` or `job`), `mutatesState`, `inputSchema`,
`outputSchema`, `artifactsSchema`, and optional `cancellation`.

Run a command:

```json
{ "type": "command_call", "id": "cmd-1", "name": "archive.preflight", "args": {} }
```

Synchronous commands return:

```json
{
  "type": "command_result",
  "id": "cmd-1",
  "status": "ok",
  "output": { "ok": true },
  "artifacts": {},
  "durationMs": 12
}
```

Failures also use `command_result` with `status: "failed"` and `error`. There is no v2
`command_error` message.

## Jobs

Job commands return `job_accepted`:

```json
{ "type": "job_accepted", "id": "cmd-1", "jobId": "job-1", "state": "running" }
```

Poll with `job_status`:

```json
{ "type": "job_status", "id": "status-1", "jobId": "job-1" }
```

While running, the response is `job_status_result`. Once terminal, `job_status` returns the terminal
`job_result` directly; clients do not send a separate `job_result` request.

Cancel with `job_cancel`, which returns `job_cancel_result`.

## Artifacts

Artifacts are named references, not bulk payloads:

```json
{
  "uri": "file:///exports/items.json",
  "path": "/exports/items.json",
  "sha256": "...",
  "byteSize": 1234,
  "contentType": "application/json",
  "finalized": true
}
```

Consumers must verify `sha256`, size, finalization, schema, and counts before trusting artifact
content. The SDK `Artifact` helper performs hash verification before returning bytes, text, JSON, or
open metadata.

## Journal

`journal_query` returns recent eval and command entries:

```json
{ "type": "journal_query", "id": "journal-1", "kind": "command", "limit": 20 }
```

Entries include `id`, `kind`, optional `name` or `code`, `success`, `durationMs`, optional
`errorKind`, and `timestamp`.
