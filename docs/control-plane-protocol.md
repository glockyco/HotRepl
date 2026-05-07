# HotRepl Control Plane Protocol

The control plane is an additive protocol beside the existing eval REPL. It is game-agnostic: HotRepl core defines transport, command/job envelopes, leases, and artifact metadata; game mods register command handlers through `IReplHost.ControlCommands`.

## Handshake

When enabled, the `handshake` message includes `controlPlane`:

```json
{
  "supported": true,
  "protocolVersion": 1,
  "authRequired": false,
  "leaseRequired": true,
  "artifactRefsSupported": true,
  "jobEventsSupported": true,
  "limits": {
    "maxMessageBytes": 1048576,
    "maxQueuedCommands": 32,
    "maxJobEventBuffer": 1000
  }
}
```

## Auth and lease flow

1. Send `control_auth` with optional `token`.
2. Receive `control_auth_result` with `ok`, `sessionId`, or structured `error`.
3. Send `lease_acquire` with `sessionId` and `clientName` before mutating commands.
4. Include returned `leaseId` on mutating `command_call`, `job_status`, `job_result`, and `job_cancel` requests.

Leases are in-memory and do not survive process restart.

## Command registry

Hosts expose descriptors with `name`, `version`, `kind` (`sync` or `job`), `mutatesState`, `argsSchema`, and `resultSchema`. Use `command_describe` to retrieve descriptors.

## Synchronous command flow

`command_call` for a `sync` descriptor executes on the engine tick path and returns either:

- `command_result` with `status: "ok"`, `result`, `artifacts`, and `diagnostics`; or
- `command_error` with `status: "failed"` and structured `error`.

## Job command flow

`command_call` for a `job` descriptor returns `command_accepted` immediately with `jobId` and initial `state: "accepted"`. Use:

- `job_status` → `job_status_result` with current `state` and optional `progress`;
- `job_result` → `job_result` after terminal completion, or `command_error` with `busy` while non-terminal;
- `job_cancel` → `job_cancel_result` with cancellation acknowledgement.

States are: `accepted`, `running`, `completed`, `failed`, `cancelling`, `cancelled`.

## Cancellation semantics

Cancellation is cooperative. `job_cancel` requests cancellation and returns immediately. A handler that observes its cancellation token transitions through `cancelling` to `cancelled`; a handler that completes first may still produce `completed`.

## Artifact references

Bulk data is not returned inline. Results include artifact metadata references:

```json
{
  "logicalName": "items",
  "uri": "file:///exports/items.json",
  "path": "/exports/items.json",
  "contentType": "application/json",
  "byteSize": 1234,
  "sha256": "...",
  "finalized": true
}
```

Consumers must independently verify artifact existence, hashes, schemas, and counts.

## Error kinds

Known control error kinds include `invalid_request`, `auth_failed`, `lease_required`, `lease_conflict`, `unknown_command`, `unsupported_operation`, `precondition_failed`, `conflict`, `busy`, `timeout`, `cancelled`, `validation_failed`, `artifact_missing`, and `internal`.

## Compatibility with eval

The existing eval protocol is unchanged. Eval responses may still use compatibility delivery behavior. Control responses are delivered only to the originating connection and are dropped if that connection is gone, preventing a replacement client from receiving another controller's results.
