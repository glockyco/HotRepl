# HotRepl Control Plane Design

## Status

Approved direction: improve HotRepl first, then build Ardenfall export automation on top of the improved game-agnostic control plane.

## Problem

HotRepl is currently a runtime C# REPL over WebSocket. It is useful for inspection and debugging, but routine automation currently has to drive it through `eval` messages containing code strings. That is not a reliable export substrate:

- results are display-oriented and may be stringified, capped, or truncated;
- cancellation is not acknowledged as a state transition;
- the server binds broadly by default and has no authentication;
- single-client replacement can make command ownership ambiguous;
- long-running work is modeled as one eval rather than a durable job;
- artifacts have no first-class protocol representation.

Ancient Kingdoms demonstrates the failure mode we want to avoid: deployment and launch are externally automated, but export order and game navigation are hardcoded in mods, while completion is inferred from logs and recent files. Ardenfall Archives should instead use HotRepl as a trustworthy local control plane with typed commands, jobs, progress, artifacts, and explicit validation.

## Goals

1. Keep HotRepl game-agnostic.
2. Preserve the existing REPL/eval workflow for diagnostics.
3. Add a first-class automation surface for typed commands and cooperative jobs.
4. Make controller ownership, cancellation, progress, errors, and artifacts machine-verifiable.
5. Default to local secure operation: loopback bind, authentication support, explicit remote opt-in.
6. Support resumable external orchestration for game exports without moving bulk data through WebSocket payloads.

## Non-goals

- HotRepl core must not reference UnityEngine, BepInEx, MelonLoader, Ardenfall, Ancient Kingdoms, or any game-specific type.
- HotRepl does not become an export framework. It provides the command/job/artifact substrate; games register their own commands.
- WebSocket artifact streaming is not the default path for large exports. Artifacts are represented by metadata and local paths first.
- Raw eval is not removed. It remains available for trusted diagnostics and discovery.

## Architecture

HotRepl becomes a dual-surface host:

```text
HotRepl WebSocket server
  ├─ REPL surface: eval/reset/complete/subscribe/select_evaluator
  └─ Control surface: command registry, command calls, jobs, artifacts, lease/auth
```

The control surface is additive. Existing clients continue to work. New clients discover support through the handshake and use typed command messages instead of code-string eval for automation.

Game integrations register command handlers through a game-agnostic interface exposed by HotRepl host adapters. Command handlers execute on the host's main-thread tick path, following the same invariant as eval: Fleck/socket threads enqueue work only; host/main thread executes game-facing logic.

## Protocol additions

### Handshake additions

The existing handshake should gain optional fields:

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

Older clients ignore this field. New clients must refuse automation if it is missing or incompatible.

### Authentication and lease

HotRepl should bind to `127.0.0.1` by default. Non-loopback bind requires explicit configuration.

A control client obtains an exclusive lease before sending control commands:

```json
{ "type": "control_auth", "id": "auth-1", "token": "..." }
{ "type": "control_auth_result", "id": "auth-1", "ok": true, "sessionId": "..." }

{ "type": "lease_acquire", "id": "lease-1", "sessionId": "...", "clientName": "ardenfall-export" }
{ "type": "lease_acquire_result", "id": "lease-1", "ok": true, "leaseId": "..." }
```

Only the lease holder may call mutating commands or control jobs. Read-only commands may later be allowed for observer clients, but the first version should keep ownership simple.

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

Schemas may be JSON Schema fragments or a simpler first-party schema format, but they must be machine-readable and versioned.

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

The server should buffer a bounded number of events per job so reconnecting clients can request events after a known sequence number.

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

The first implementation only needs local file references. Download/stream APIs can come after command/job semantics are stable.

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

HotRepl core should define game-agnostic abstractions similar to:

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

Host adapters provide an optional registry through `IReplHost`, or through a new adjacent interface if keeping `IReplHost` smaller is cleaner.

Command handlers must not execute on Fleck threads. They are scheduled through the existing main-thread tick discipline.

## Client API

The Python client should expose control-plane methods:

```python
async with hotrepl.connect(url, token=token) as client:
    await client.acquire_lease("ardenfall-export")
    info = await client.call("archive.info", {})
    job = await client.start_job("entity.exportBatch", args)
    async for event in client.job_events(job.id):
        ...
    result = await client.job_result(job.id)
```

The CLI should expose generic control operations:

```sh
hotrepl control describe
hotrepl control call archive.preflight '{}'
hotrepl control job-status <job-id>
hotrepl control cancel <job-id>
```

Game-specific CLIs, such as `ardenfall-export`, should use the Python client library rather than shelling out to `hotrepl eval`.

## Backward compatibility

- Keep all existing message types and CLI commands.
- Add handshake fields; do not require old clients to understand them.
- Raw eval remains available by default for local trusted workflows.
- Control-plane auth/lease applies to control messages first. A later hardening pass may role-gate eval.
- Existing BepInEx/MelonLoader hosts can start with no registered commands; they simply advertise no control registry.

## Ardenfall target workflow enabled by this design

Ardenfall Archives will register compiled commands:

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

The external orchestrator will:

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

- **Typed control protocol instead of eval wrappers:** avoids ad hoc C# strings, result truncation, persistent evaluator state, and weak command discovery.
- **Jobs from the start:** export and screenshot operations are naturally long-running; adding jobs later would force protocol churn.
- **Artifacts by reference:** exported data belongs on disk with hashes and counts, not in display-oriented WebSocket result payloads.
- **Exclusive lease:** prevents two controllers from mutating game/export state concurrently.
- **Loopback/auth defaults:** HotRepl controls a live game process and can run arbitrary code; safe defaults matter.
- **Additive migration:** existing diagnostics workflows keep working while automation moves to the control surface.

## Risks and mitigations

- **More protocol surface:** keep the first version small: auth, lease, describe, call, job, artifact refs.
- **Command authors must cooperate with cancellation:** document handler requirements and add tests with cancellable fake handlers.
- **Reconnect semantics can become complex:** first version may require reconnect + `job_status`; full controller reattach can follow once ownership rules are proven.
- **Remote debugging becomes less convenient:** allow explicit non-local bind/auth config, but never default to it.
