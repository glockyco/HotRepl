---
title: "HotRepl Control Plane Design"
type: spec
status: superseded
created: 2026-05-06
parent:
superseded_by: 2026-05-22-hotrepl-clean-architecture-design
archived: 2026-06-25
---

# HotRepl Control Plane Design

## Status

HotRepl provides a game-agnostic control plane for typed automation. Ardenfall export automation
builds on that control plane.

## Problem

HotRepl is a runtime C# REPL and typed control plane over WebSocket. The eval REPL is useful for
inspection and debugging; routine automation uses typed control messages rather than `eval` messages
containing code strings. Control messages provide a reliable export substrate:

- results are structured objects rather than display strings;
- cancellation is acknowledged as a job state transition;
- the server binds to loopback by default and supports authentication;
- control responses belong to the originating connection;
- long-running work is modeled as durable in-memory jobs;
- artifacts use protocol metadata references.

Ancient Kingdoms demonstrates the failure mode this design avoids: deployment and launch are
externally automated, but export order and game navigation are hardcoded in mods, while completion
is inferred from logs and recent files. Ardenfall Archives uses HotRepl as a trustworthy local
control plane with typed commands, jobs, progress, artifacts, and explicit validation.

## Goals

1. Keep HotRepl game-agnostic.
2. Keep the REPL/eval workflow available for diagnostics.
3. Provide a first-class automation surface for typed commands and cooperative jobs.
4. Make controller ownership, cancellation, progress, errors, and artifacts machine-verifiable.
5. Default to local secure operation: loopback bind, authentication support, explicit remote opt-in.
6. Support resumable external orchestration for game exports without moving bulk data through
   WebSocket payloads.

## Non-goals

- HotRepl core must not reference UnityEngine, BepInEx, MelonLoader, Ardenfall, Ancient Kingdoms, or
  any game-specific type.
- HotRepl does not become an export framework. It provides the command/job/artifact substrate; games
  register their own commands.
- WebSocket artifact streaming is not the default path for large exports. Artifacts are represented
  by metadata and local paths first.
- Raw eval is not removed. It remains available for trusted diagnostics and discovery.

## Architecture

HotRepl becomes a dual-surface host:

```text
HotRepl WebSocket server
  ├─ REPL surface: eval/reset/complete/subscribe/select_evaluator
  └─ Control surface: command registry, command calls, jobs, artifacts, lease/auth
```

HotRepl exposes both surfaces through the same WebSocket connection. Clients discover control-plane
support through the handshake and use typed command messages instead of code-string eval for
automation.

Game integrations register command handlers through a game-agnostic interface exposed by HotRepl
host adapters. Command handlers execute on the host's main-thread tick path, following the same
invariant as eval: Fleck/socket threads enqueue work only; host/main thread executes game-facing
logic.

## Protocol additions

### Handshake additions

The handshake includes optional control-plane fields:

```json
{
  "controlPlane": {
    "supported": true,
    "protocolVersion": 1,
    "authRequired": true,
    "leaseRequired": true,
    "artifactRefsSupported": true,
    "jobEventsSupported": true,
    "limits": {
      "maxMessageBytes": 1048576,
      "maxInFlightCommands": 1,
      "maxQueuedCommands": 32,
      "maxJobEventBuffer": 1000
    }
  }
}
```

Clients that do not use the control plane ignore this field. Automation clients require compatible
control-plane metadata.

### Authentication and lease

HotRepl binds to `127.0.0.1` by default. Non-loopback bind requires explicit configuration.

A control client obtains an exclusive lease before sending control commands:

```json
{ "type": "control_auth", "id": "auth-1", "token": "..." }
{ "type": "control_auth_result", "id": "auth-1", "ok": true, "sessionId": "..." }

{ "type": "lease_acquire", "id": "lease-1", "sessionId": "...", "clientName": "ardenfall-export" }
{ "type": "lease_acquire_result", "id": "lease-1", "ok": true, "leaseId": "..." }
```

Only the lease holder may call mutating commands or control jobs. Read-only commands can run without
a lease when the command descriptor does not mutate state.

### Command registry

Clients discover commands with:

```json
{ "type": "command_describe", "id": "describe-1" }
```

Response:

```json
{
  "type": "command_describe_result",
  "id": "describe-1",
  "commands": [
    {
      "name": "archive.preflight",
      "version": 1,
      "kind": "sync",
      "mutatesState": false,
      "argsSchema": { "type": "object", "additionalProperties": false },
      "resultSchema": { "type": "object" }
    }
  ]
}
```

Schemas may be JSON Schema fragments or a simpler first-party schema format, but they must be
machine-readable and versioned.

### Synchronous command call

For short commands:

```json
{
  "type": "command_call",
  "id": "cmd-1",
  "leaseId": "...",
  "name": "archive.preflight",
  "args": {},
  "timeoutMs": 5000,
  "idempotencyKey": "run-123/preflight/1"
}
```

Success:

```json
{
  "type": "command_result",
  "id": "cmd-1",
  "status": "ok",
  "result": { "passed": true, "checks": [] },
  "artifacts": [],
  "diagnostics": []
}
```

Failure:

```json
{
  "type": "command_error",
  "id": "cmd-1",
  "status": "failed",
  "error": {
    "kind": "precondition_failed",
    "code": "worldDataUnavailable",
    "message": "ArdenfallGame.instance.worldData is null",
    "retryable": true,
    "details": {}
  },
  "diagnostics": []
}
```

### Job command call

For long-running commands, `command_call` returns a job:

```json
{
  "type": "command_accepted",
  "id": "cmd-2",
  "jobId": "job-123",
  "state": "accepted"
}
```

Job states:

```text
accepted -> running -> completed
accepted -> cancelled
running -> cancelling -> cancelled
running -> failed
```

Clients query:

```json
{ "type": "job_status", "id": "job-status-1", "leaseId": "...", "jobId": "job-123" }
{ "type": "job_result", "id": "job-result-1", "leaseId": "...", "jobId": "job-123" }
```

Cancellation is acknowledged:

```json
{ "type": "job_cancel", "id": "cancel-1", "leaseId": "...", "jobId": "job-123" }
{ "type": "job_cancel_result", "id": "cancel-1", "accepted": true, "state": "cancelling" }
```

### Job events

Jobs emit correlated lifecycle/progress events:

```json
{
  "type": "job_event",
  "jobId": "job-123",
  "sequence": 4,
  "state": "running",
  "progress": { "phase": "exporting", "current": 250, "total": 1000 },
  "message": "Exported item batch 1"
}
```

The server buffers a bounded number of events per job so reconnecting clients can request events
after a known sequence number.

### Artifact references

Command and job results may include artifacts:

```json
{
  "logicalName": "items.part-0001.json",
  "uri": "file:///.../items.part-0001.json",
  "path": "/.../items.part-0001.json",
  "contentType": "application/json",
  "byteSize": 12345,
  "sha256": "...",
  "finalized": true
}
```

The protocol uses local file references for artifacts. Download/stream APIs are outside the
control-plane command/job contract.

## Error model

Control-plane errors use stable categories:

- `invalid_request`
- `auth_failed`
- `lease_required`
- `lease_conflict`
- `unknown_command`
- `unsupported_operation`
- `precondition_failed`
- `conflict`
- `busy`
- `timeout`
- `cancelled`
- `validation_failed`
- `artifact_missing`
- `internal`

Every error includes:

```json
{
  "kind": "precondition_failed",
  "code": "worldDataUnavailable",
  "message": "Human-readable detail",
  "retryable": true,
  "details": {}
}
```

## Command handler API

HotRepl core defines game-agnostic abstractions:

```csharp
public interface IControlCommandRegistry
{
    IReadOnlyList<ControlCommandDescriptor> Describe();
    bool TryGet(string name, out IControlCommandHandler handler);
}

public interface IControlCommandHandler
{
    ControlCommandDescriptor Descriptor { get; }
    ControlCommandKind Kind { get; }
    ValueTask<ControlCommandResult> ExecuteAsync(ControlCommandContext context, JObject args, CancellationToken cancellationToken);
}
```

Host adapters provide a command registry through `IReplHost.ControlCommands`.

Command handlers do not execute on Fleck threads. They are scheduled through the main-thread tick
discipline.

## Client API

The Python client exposes control-plane methods:

```python
async with hotrepl.connect(url, token=token) as client:
    await client.acquire_lease("ardenfall-export")
    info = await client.call("archive.info", {})
    job = await client.start_job("entity.exportBatch", args)
    async for event in client.job_events(job.id):
        ...
    result = await client.job_result(job.id)
```

The CLI exposes generic control operations:

```sh
hotrepl control describe
hotrepl control call archive.preflight '{}'
hotrepl control job-status <job-id>
hotrepl control cancel <job-id>
```

Game-specific CLIs, such as `ardenfall-export`, use the Python client library rather than shelling
out to `hotrepl eval`.

## Compatibility

- Eval, reset, completion, subscription, evaluator selection, and CLI diagnostic commands remain
  available.
- Handshake control-plane fields are optional for clients that do not use automation.
- Raw eval remains available by default for local trusted workflows.
- Control-plane auth/lease applies to control messages.
- BepInEx/MelonLoader hosts can advertise an empty command registry.

## Ardenfall target workflow enabled by this design

Ardenfall Archives registers compiled commands:

```text
archive.info
archive.preflight
run.begin
run.status
entity.plan
entity.exportBatch
run.finalize
run.discard
game.quit
```

The external orchestrator:

1. deploy HotRepl and Ardenfall export mod;
2. launch Ardenfall;
3. wait for HotRepl;
4. authenticate and acquire lease;
5. validate command registry;
6. poll `archive.preflight`;
7. begin a run;
8. export entity batches;
9. finalize snapshot;
10. independently validate artifacts and manifest;
11. run pipeline ingestion.

## Design choices and justification

- **Typed control protocol instead of eval wrappers:** avoids ad hoc C# strings, result truncation,
  persistent evaluator state, and weak command discovery.
- **Jobs:** export and screenshot operations are naturally long-running and use explicit job states.
- **Artifacts by reference:** exported data belongs on disk with hashes and counts, not in
  display-oriented WebSocket result payloads.
- **Exclusive lease:** prevents two controllers from mutating game/export state concurrently.
- **Loopback/auth defaults:** HotRepl controls a live game process and can run arbitrary code; safe
  defaults matter.
- **Dual-surface protocol:** diagnostics use the eval REPL; automation uses the typed control
  surface.

## Risks and mitigations

- **Protocol surface:** the control plane keeps the protocol small: auth, lease, describe, call,
  job, artifact refs.
- **Command authors must cooperate with cancellation:** handler requirements are documented and
  covered by cancellable fake-handler tests.
- **Reconnect semantics:** clients use reconnect plus `job_status`; controller reattach requires a
  valid lease and job id.
- **Remote debugging becomes less convenient:** allow explicit non-local bind/auth config, but never
  default to it.
