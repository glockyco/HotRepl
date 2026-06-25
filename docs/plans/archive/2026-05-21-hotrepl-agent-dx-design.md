---
title: "HotRepl Agent DX Design"
type: spec
status: implemented
created: 2026-05-21
parent:
superseded_by:
archived: 2026-06-25
---

# HotRepl Agent DX Design

## Status

HotRepl already has the right substrate for trustworthy game automation: a WebSocket REPL, a typed
control plane, authentication, exclusive leases, cooperative jobs, structured errors, and artifact
references. The remaining problem is the public operator surface. Agents still have to compose
low-level commands, scrape config files, pass tokens through shell arguments, poll readiness by
retrying full exports, and stitch game-specific launch/deploy/export steps outside HotRepl.

This revision keeps the architectural direction but narrows the first implementation cut. HotRepl
owns generic discovery, auth, connection-scoped lease authority, readiness, one-connection control
command execution, job supervision, and machine-readable CLI conventions. Game repositories own
their profiles, launch commands, game-specific control handlers, export workflows, and site
validation.

## Problem

The current workflow is reliable once everything is already running, but brittle to invoke:

1. A caller must know or infer the WebSocket URL.
2. A caller must locate the game config file and extract the control auth token.
3. A caller must remember when a lease is required and keep that lease on the same connection.
4. Long-running job calls require separate start/status/result invocations, which lose session and
   lease state between processes.
5. The handshake advertises some control-plane capabilities that do not fully match enforcement or
   client-facing APIs.
6. There is no first-class readiness command that separates socket startup, REPL liveness,
   control-plane readiness, current-connection lease readiness, and game-world readiness.
7. CLI output and error shapes vary across commands, forcing agents to parse human text.

The result is shell glue where the agent assembles paths, tokens, launch commands, sleeps/retries,
and export validation manually. That is exactly the kind of workflow HotRepl should hide behind a
small, stable automation surface.

## Goals

1. Make a running HotRepl instance discoverable without hard-coded ports or config scraping.
2. Keep secrets out of stdout, logs, process lists, and instance discovery documents.
3. Make auth and lease acquisition first-class in the Python client and CLI.
4. Enforce control authority from the current WebSocket connection and authenticated session, not
   from caller-replayed bearer identifiers.
5. Provide a deterministic readiness model with structured checks, blocked/unobserved states, and
   remediation.
6. Provide one-shot control command/job execution that keeps a single connection/session/lease.
7. Standardize CLI JSON/JSONL output, error envelopes, and exit-code categories.
8. Make advertised control-plane capabilities match what clients can actually consume.
9. Preserve HotRepl's game-agnostic core and platform-boundary invariants.

## Non-goals

- HotRepl will not learn how to launch CrossOver, Steam, Ardenfall, or any other game.
- HotRepl will not become an export framework. Game mods still register their own commands.
- HotRepl will not print auth tokens in normal JSON output.
- HotRepl will not replace raw eval. Eval remains the diagnostic surface.
- HotRepl will not stream bulk artifacts over WebSocket by default. Artifacts remain references with
  local paths, URIs, hashes, sizes, and finalized flags.
- This design does not require a full MCP server. It adopts MCP-compatible schema/discovery ideas
  where they help agents.
- The first implementation cut will not add reconnectable job watch, cross-process job follow,
  same-socket re-auth, or replacement-client terminal result recovery.

## Design principles

### Boring commands over shell recipes

Common automation should be one command with stable flags, not a README block. Low-level commands
remain available, but primary workflows should avoid manual token extraction, lease choreography,
and multi-process job polling.

### Discover, then act

Agents need to ask “what is running?” and “what can I safely do?” before mutation. HotRepl should
support static help, runtime discovery, and machine-readable readiness checks.

### Health is staged

A TCP/WebSocket listener does not mean the game is ready. The readiness model separates startup,
liveness, control readiness, lease readiness, command readiness, and host/game readiness. This
matches the same distinction made by liveness/readiness/startup probes in production systems.

### Structured output is a contract

Machine-readable output goes to stdout; diagnostics go to stderr; success exits zero; failures exit
non-zero. Long-running workflows emit JSON Lines events with a single terminal event.

### Secrets are handles, not strings

Automation may need to use a token, but it should not print or require shell-visible token values.
Profiles and instance documents expose token handles, fingerprints, and status, not token contents.

## Architecture

```text
HotRepl core
  ├─ WebSocket REPL/control server
  ├─ instance discovery document writer
  ├─ control handshake metadata
  ├─ connection-scoped auth/session/lease manager
  ├─ control command router
  ├─ job manager + current-connection events
  └─ status/readiness command data

HotRepl Python client / CLI
  ├─ profile and instance resolution
  ├─ auth token handle resolution
  ├─ active/passive command policy
  ├─ lease acquisition/release/status
  ├─ doctor/wait/status commands
  ├─ one-shot control run/job follow
  └─ standardized outcome renderer for JSON/JSONL/human output

Game-specific repository
  ├─ local profile file
  ├─ deploy/build/launch commands
  ├─ game-specific control command handlers
  ├─ export orchestration
  └─ artifact/site validation
```

HotRepl remains the reusable control substrate. A game repo such as Ardenfall Compendium consumes
that substrate to offer its own command, for example `bun run live:export --profile crossover`.

## Instance discovery

When HotRepl starts, it writes a user-only candidate instance document and removes it on clean
dispose. The file path is platform-specific but stable:

```text
$XDG_RUNTIME_DIR/hotrepl/instances/<instance-id>.json
~/Library/Application Support/HotRepl/instances/<instance-id>.json
%LOCALAPPDATA%/HotRepl/instances/<instance-id>.json
```

The file must be written atomically with a temporary file plus rename. Permissions must be
user-readable/writable only: POSIX mode where available and equivalent restrictive ACLs on Windows.
The file contains no token values:

```json
{
  "schemaVersion": 1,
  "instanceId": "ardenfall-demo-18590-20260521T053738Z",
  "url": "ws://127.0.0.1:18590",
  "bindHost": "127.0.0.1",
  "port": 18590,
  "startedAt": "2026-05-21T05:37:38Z",
  "process": { "id": 12345, "name": "Ardenfall.exe" },
  "host": { "name": "BepInEx", "runtime": "Mono", "platform": "Unity" },
  "controlPlane": {
    "supported": true,
    "protocolVersion": 1,
    "authRequired": true,
    "leaseRequired": true
  },
  "auth": {
    "required": true,
    "fingerprint": "sha256:12ab34cd"
  }
}
```

The core instance document reports endpoint, host, process, and control-plane facts that HotRepl can
know without platform coupling. It may include a non-secret token fingerprint because `ReplConfig`
already contains the token value. It does not include a BepInEx config path, section, key, Keychain
entry, or other auth-source handle unless the host adapter provides that metadata through an
explicit HotRepl host-boundary API. Client profiles remain the primary source for auth handles.

A discovery document is a candidate, not proof of liveness or authority. Active commands must still
confirm the socket and handshake before trusting it. Stale cleanup should require positive evidence
such as a failed loopback connection to the documented URL; process id reuse alone is not enough to
delete another instance's document.

The CLI resolves instances with:

```sh
hotrepl discover --json
hotrepl discover --host BepInEx --json
hotrepl discover --profile ardenfall-demo --json
```

If multiple instances match, human output explains the ambiguity. JSON output returns all matches
and sets `ok:false` for commands that require a single target.

## Profiles and auth handles

A HotRepl profile is a local client-side resolution hint, not a game-specific feature in core. It
can map a friendly name to URL, instance filters, and auth sources:

```json
{
  "schemaVersion": 1,
  "profiles": {
    "ardenfall-demo": {
      "instance": { "hostName": "BepInEx", "port": 18590 },
      "url": "ws://127.0.0.1:18590",
      "auth": {
        "source": "bepinex-config",
        "path": "~/Library/Application Support/CrossOver/.../BepInEx/config/hotrepl.bepinex.cfg",
        "section": "Control",
        "key": "AuthToken"
      }
    }
  }
}
```

Supported initial auth sources:

- environment variable: useful in CI or temporary shells;
- token file: a user-only file containing just the secret;
- BepInEx config key: compatibility with existing deployments;
- explicit `--token` for escape hatches only, marked as unsafe in help text because argv can be
  visible to other processes.

Normal JSON output reports `auth.required`, `auth.source`, and `auth.fingerprint`; it never includes
`auth.token`.

## Security posture

Improving discovery removes accidental obscurity, so the control plane must be explicit about unsafe
configurations:

- The default bind host remains loopback. In v1, control-plane automation over plain `ws://` is safe
  only on loopback. Non-loopback binds must either disable the control plane, run behind an
  operator-provided secure tunnel/transport, or require an explicitly named dangerous override; a
  bearer token over cleartext `ws://` is not sufficient protection.
- `authRequired:false` is reported as an insecure/degraded control posture in `status` and `doctor`.
  High-level mutating CLI workflows should refuse unauthenticated control by default unless the user
  passes an explicit override.
- Token fingerprints are correlation/debugging aids only. They never prove authorization.
- `--token` remains an escape hatch because command-line arguments may be visible to other
  processes; profiles should prefer token files, environment handles, or host-specific config
  readers.

## Control auth and lease UX

The Python client gains a reusable connection setup method:

```python
await client.prepare_control(
    token_source=profile.auth,
    client_name="hotrepl-cli",
    acquire_lease=True,
    require_commands=["compendium.preflight", "run.begin"],
)
```

The CLI exposes the same flow:

```sh
hotrepl control describe --profile ardenfall-demo --json
hotrepl control call compendium.preflight '{}' --profile ardenfall-demo --json
hotrepl control run entity.exportBatch '{"runId":"...","entity":"item","offset":0,"limit":100}' \
  --profile ardenfall-demo \
  --lease \
  --wait \
  --jsonl
```

Rules:

1. Read-only commands authenticate when required but do not acquire a lease unless requested.
2. Mutating commands acquire a lease by default when the server requires one.
3. `control_auth` is one-shot per WebSocket connection. A second `control_auth` on an already
   authenticated connection is rejected with `conflict/alreadyAuthenticated`; callers that need a
   different identity reconnect. This avoids same-socket session revocation while jobs or leases may
   still exist.
4. Control authority is derived from the current WebSocket connection and its authenticated session.
   `sessionId` and `leaseId` may remain in protocol responses for diagnostics and correlation, but
   they are not bearer credentials that grant authority when replayed from another connection.
   Mutating command paths must validate that the current connection's authenticated session owns the
   active lease.
5. Jobs are bound to the authenticated session and connection that accepted them. Lease-required
   jobs are additionally bound to the accepting lease. `job_status`, `job_result`, and `job_cancel`
   for those jobs must be rejected from any other session or replacement connection.
6. Job status/result/cancel operations for automation remain inside the existing connection in
   `control run`. Standalone job follow subcommands are out of scope for lease-required jobs under
   HotRepl's single-client model; implementations should expose job follow as same-process,
   same-connection client methods rather than separate CLI invocations.
7. Ctrl-C on `control run --wait` sends `job_cancel` when a job has been accepted, then keeps the
   connection open until it receives `job_cancel_result` and a terminal job state. If a mutating job
   has no observed terminal state before the interrupt grace timeout, the CLI exits with a
   structured non-retryable interruption error. If a non-mutating job has no observed terminal state
   before the grace timeout, the CLI exits with a structured retryable abandoned-read result because
   no host mutation can be duplicated by retrying the read-only command.
8. If a connection closes or is replaced while its session owns non-terminal jobs, HotRepl requests
   cancellation for those jobs. Non-lease jobs are abandoned with no replacement-client result
   recovery; callers that need a read-only job result must keep the original `control run`
   connection alive. Non-terminal lease-bound orphaned jobs keep the lease pinned until they reach a
   terminal state or the orphan grace window expires.
9. Replacement clients can inspect lightweight lease-bound orphaned job summaries through
   `lease_status`. They cannot fetch the original terminal payload and cannot receive addressed
   responses from the previous controller. Terminal orphan summaries may remain in bounded
   diagnostic retention, but v1 does not expose `orphaned_job_result`, `orphaned_job_ack`, or any
   other cross-connection terminal result replay.
10. If a lease-bound orphaned job does not reach a terminal state before the configured orphan grace
    window, `lease_status` reports `recoveryRequired: "restartHost"`; HotRepl does not automatically
    release the lease because the job may still be mutating host state.
11. Once orphaned work requires host restart, new mutating lease acquisition fails with
    non-retryable `conflict/restartHostRequired`, and lease-readiness checks report the same
    remediation instead of treating the condition as a transient wait.
12. Game-specific mutating commands should use explicit run ids, idempotency keys, artifact
    references, and validation commands when they need recovery after an interrupted controller.
    HotRepl provides the transport and safety state; game repos decide how to reconcile their own
    artifacts.

Lease observability requires explicit protocol messages rather than parsing conflict strings:

```json
{ "type": "lease_status", "id": "lease-status-1" }
{
  "type": "lease_status_result",
  "id": "lease-status-1",
  "leaseRequired": true,
  "heldByCaller": true,
  "lease": {
    "clientName": "hotrepl-cli",
    "createdAt": "2026-05-21T05:37:38Z"
  },
  "orphanedJobs": [
    { "jobId": "job-123", "state": "cancelling", "terminal": false, "pinsLease": true }
  ],
  "recoveryRequired": null
}

{ "type": "lease_release", "id": "lease-release-1" }
{ "type": "lease_release_result", "id": "lease-release-1", "released": true }
```

`lease_status` and `lease_release` infer the authenticated session from the current WebSocket
connection; they do not accept arbitrary session ids. They do not attempt to inspect another live
controller's lease because HotRepl's single-client model disconnects the previous client when a new
one connects. They may report retryable orphaned-work state caused by non-terminal lease-bound jobs
from a disconnected prior session. `lease_release` releases only the caller's own active lease for
the authenticated session and fails with `conflict/nonTerminalJobs` while any job accepted under
that lease is still in a non-terminal state.

## Readiness and doctor model

HotRepl adds three complementary commands:

```sh
hotrepl status --profile ardenfall-demo --json
hotrepl wait --profile ardenfall-demo --lease --commands compendium.preflight,run.begin --json
hotrepl doctor --profile ardenfall-demo --json
```

`discover` is passive: it reads local instance documents and does not open a WebSocket. `status`,
`wait`, `doctor`, and `control describe` become active when they connect to the server. Machine
output must disclose both that they open a WebSocket and that HotRepl's single-client model may
replace an existing controller connection. Active diagnostic commands authenticate when required but
do not acquire or release the lease unless the caller explicitly asks for a lease check.
`wait --lease` acquires the lease only after earlier required checks have passed and the command is
otherwise ready to report success.

`status` is a fast point-in-time read. `wait` retries retryable checks until ready or timeout.
`doctor` runs all configured checks and returns every result it can observe. If an earlier
dependency prevents a later check from running, the later check is `blocked` or `unobserved`, not
failed.

Check result shape:

```json
{
  "name": "control.lease",
  "status": "pass",
  "severity": "required",
  "retryable": true,
  "connectionImpact": {
    "mode": "active-websocket",
    "mayReplaceActiveClient": true,
    "acquiresLease": false
  },
  "observed": { "leaseRequired": true, "heldByCaller": true },
  "remediation": "Acquire a lease before sending mutating control commands."
}
```

Standard check groups:

| Group      | Examples                                                            |
| ---------- | ------------------------------------------------------------------- |
| `instance` | discovery document exists, process appears alive, URL selected      |
| `socket`   | WebSocket connects, handshake received                              |
| `repl`     | evaluator initialized, evaluator error absent, ping succeeds        |
| `control`  | control plane supported, protocol version compatible, auth succeeds |
| `lease`    | lease required/acquired/released for the current connection         |
| `commands` | required command names, versions, and kinds match expectations      |
| `host`     | optional host-provided readiness summary                            |

Game readiness remains game-owned. HotRepl can expose generic host readiness data; game repos should
compose it with commands such as `compendium.preflight`.

## Handshake capability alignment

The control-plane handshake must describe actual client-visible behavior:

| Field                      | True when                                                       | Safe client assumption                                            |
| -------------------------- | --------------------------------------------------------------- | ----------------------------------------------------------------- |
| `authRequired`             | the server rejects unauthenticated control sessions             | a token source is required before control commands                |
| `leaseRequired`            | `RequireControlLease` is enabled for mutating commands          | mutating commands need a lease owned by the current session       |
| `jobEventsSupported`       | current-connection job events are documented and consumable     | `control run --wait` may follow events on its existing connection |
| `jobEventReplaySupported`  | replay after a known sequence is documented and implemented     | reconnect/replay clients may request missed events                |
| `artifactRefsSupported`    | results contain artifact references rather than inline payloads | consumers must verify artifacts out of band                       |
| `limits.maxMessageBytes`   | inbound control message budget enforced by the server           | clients must keep requests below the advertised byte limit        |
| `limits.maxQueuedCommands` | queue overload is enforced by the server                        | clients should back off on busy/overload responses                |
| `limits.maxJobEventBuffer` | per-job event buffer is enforced by the server                  | clients may lose older events after the advertised buffer size    |

`controlPlane.protocolVersion` versions the WebSocket control-plane wire contract. CLI/instance
document `schemaVersion` fields version those serialized documents only. Capability bits are
additive feature switches: absence or false means unsupported. Any incompatible change to an
existing message meaning, including `job_result` terminal payload shape or acknowledgement
requirements, must either add new messages or bump the control-plane protocol version.

## Job supervision and event replay

HotRepl already models jobs. The client-facing UX should complete the same-connection job workflow
without turning the first cut into a reconnectable job service:

```sh
hotrepl control run long.command '{...}' --wait --jsonl
```

Event shape:

```json
{
  "schemaVersion": 1,
  "type": "job_event",
  "jobId": "job-123",
  "sequence": 43,
  "state": "running",
  "progress": { "phase": "export", "current": 500, "total": 1273 },
  "message": "Exported item batch 5"
}
```

Job event follow is scoped to the same process and WebSocket connection as `control run --wait`.
Standalone `job watch`/reconnectable follow is out of scope until HotRepl has durable session and
lease semantics that survive a client reconnect without violating the single-client invariant. If
current-connection event delivery is not implemented in a slice, HotRepl must advertise
`jobEventsSupported:false` for that build. Replay/reconnect support uses a separate
`jobEventReplaySupported` capability and remains false for the first cut.

The first implementation should preserve existing `job_result` wire semantics unless it deliberately
introduces a new protocol version or new message family. The CLI can still emit exactly one JSONL
terminal envelope by translating the terminal state it observes on the original connection. If the
connection is lost before a terminal state is observed, the result is not recoverable from a
replacement client in v1.

## Standard CLI event and error envelopes

Short `--json` commands emit one object:

```json
{
  "schemaVersion": 1,
  "ok": true,
  "command": "hotrepl.status",
  "data": {},
  "meta": { "durationMs": 25 }
}
```

Long `--jsonl` commands emit JSON Lines with a terminal event exactly once. They do not switch to
the short-command one-object envelope on failure or cancellation.

Successful stream:

```json
{"schemaVersion":1,"command":"hotrepl.control.run","phase":"connect","status":"completed","timestamp":"..."}
{"schemaVersion":1,"command":"hotrepl.control.run","phase":"job","status":"running","progress":{"current":5,"total":10}}
{"schemaVersion":1,"command":"hotrepl.control.run","phase":"complete","status":"completed","result":{},"diagnostics":[],"artifacts":[]}
```

Alternative terminal events for failure and cancellation:

```json
{"schemaVersion":1,"command":"hotrepl.control.run","phase":"complete","status":"failed","error":{"kind":"command_failed","code":"jobFailed","message":"Job failed.","retryable":false},"diagnostics":[],"artifacts":[]}
{"schemaVersion":1,"command":"hotrepl.control.run","phase":"complete","status":"cancelled","error":{"kind":"cancelled","code":"jobCancelled","message":"Job cancelled after Ctrl-C.","retryable":false},"diagnostics":[],"artifacts":[]}
{"schemaVersion":1,"command":"hotrepl.control.run","phase":"complete","status":"interrupted","error":{"kind":"interrupted","code":"interruptUnconfirmed","message":"Cancellation was requested for a mutating job, but no terminal job state was observed before the interrupt grace timeout. Do not retry until the previous job's terminal state is confirmed.","retryable":false}}
{"schemaVersion":1,"command":"hotrepl.control.run","phase":"complete","status":"abandoned","error":{"kind":"cancelled","code":"readOnlyJobAbandoned","message":"Cancellation was requested for a non-mutating job, but no terminal job state was observed before the interrupt grace timeout. The read-only command can be retried.","retryable":true}}
```

The first implementation cut does not add terminal-result acknowledgement. A same-connection client
emits its terminal JSONL envelope after observing a terminal state and then exits. If the connection
is lost before terminal state is observed, automation must use the interruption/abandonment exit
code and any game-owned idempotency key, artifact reference, or validation command to decide
recovery. Future terminal acknowledgement, replay, or replacement-client result retrieval requires a
new protocol version or additive message family.

Short-command failure envelope:

```json
{
  "schemaVersion": 1,
  "ok": false,
  "command": "hotrepl.wait",
  "error": {
    "kind": "timeout",
    "code": "readinessTimeout",
    "message": "Timed out waiting for control commands: run.begin missing.",
    "retryable": true,
    "remediation": "Verify the game-specific plugin registered its control commands."
  }
}
```

Exit-code categories:

| Exit code | Meaning                                                    |
| --------- | ---------------------------------------------------------- |
| 0         | success                                                    |
| 1         | unexpected/internal failure                                |
| 2         | invalid CLI usage or invalid input JSON                    |
| 3         | server unreachable                                         |
| 4         | auth failed                                                |
| 5         | lease acquisition failed                                   |
| 6         | readiness timeout or failed required check                 |
| 7         | command/job failed                                         |
| 8         | confirmed cancellation                                     |
| 9         | best-effort interruption; terminal job state not confirmed |
| 10        | abandoned non-mutating job; safe to retry                  |

## Command schemas and agent discovery

HotRepl command descriptors already include `argsSchema` and `resultSchema`. The CLI should preserve
and print those schemas exactly. Game integrations should avoid loose `{"type":"object"}` schemas
for production automation commands; concrete schemas let agents validate before mutating state.

HotRepl should also expose static CLI command metadata in a machine-readable form:

```sh
hotrepl tools --json
```

This is not full MCP, but the shape should align with MCP tool ideas where practical. MCP-style
annotations are advisory metadata for agents; HotRepl still enforces safety from command
descriptors, session ownership, and leases:

```json
{
  "name": "hotrepl.control.run",
  "description": "Run a HotRepl control command or job through one authenticated lease-holding connection.",
  "inputSchema": { "type": "object", "properties": {} },
  "outputSchema": { "type": "object", "properties": {} },
  "annotations": {
    "readOnlyHint": false,
    "destructiveHint": true,
    "idempotentHint": false,
    "openWorldHint": false
  }
}
```

## Game repository integration example

With the HotRepl pieces in place, a game repo can expose a boring workflow without reimplementing
transport concerns:

```sh
bun run live:doctor --profile crossover --json
bun run live:export --profile crossover --sync-site --build-site --jsonl
```

That repo owns:

- CrossOver/Steam/game launch;
- mod and HotRepl DLL deployment;
- local profile defaults;
- game-specific command schemas;
- export/pipeline/site validation;
- browser or smoke checks.

HotRepl owns:

- endpoint discovery;
- token handle resolution;
- control auth;
- connection-scoped leases;
- command/job transport;
- current-connection job progress/events;
- generic readiness and error envelopes.

## Testing strategy

### HotRepl core tests

- Control authority validates the current connection's authenticated session, not only a caller
  supplied `leaseId`.
- Same-connection re-auth is rejected after a connection has authenticated.
- Handshake metadata matches config for auth, lease, artifact references, job-event support, and
  replay support.
- Instance document writes are atomic, contain no secret values, use restrictive permissions/ACLs,
  and are removed on clean dispose.
- Discovery document writer handles stale/unwritable paths with structured diagnostics.
- Lease status/release paths preserve exclusive mutating-command behavior and report orphaned
  lease-bound work without exposing terminal result payloads.
- Job event support is either fully reachable through protocol messages or advertised as false.

### Python client tests

- CLI handlers return typed outcomes/events and one renderer produces human, JSON, and JSONL output.
- Profile resolution chooses URL/auth source without printing secrets.
- `prepare_control` authenticates and acquires leases when required.
- Active commands expose their connection impact and single-client takeover risk; passive discovery
  does not open a WebSocket.
- `status`, `wait`, and `doctor` report blocked/unobserved checks when dependencies fail.
- `control run --wait` keeps one connection and returns final job result for completed jobs.
- Ctrl-C cancellation sends `job_cancel` for accepted jobs and distinguishes confirmed cancellation,
  non-retryable mutating interruption, and retryable abandoned non-mutating jobs.
- JSON/JSONL success and error envelopes match snapshots.
- Exit codes map to unreachable/auth/lease/readiness/command/cancel/interrupted/abandoned
  categories, including exit code 10 for abandoned non-mutating jobs.

### Smoke tests against a running game

- `hotrepl discover --json` finds the running instance without opening a WebSocket.
- `hotrepl wait --profile <name> --json` reaches ready state and reports active connection impact
  plus single-client takeover risk.
- `hotrepl control describe --profile <name> --json` returns concrete command descriptors.
- A read-only control call succeeds without a lease when leases are not required for read-only
  commands.
- A mutating job can be run with `control run --lease --wait --jsonl`.

## Rollout plan

1. Rewrite control session/lease authority so ownership is connection/session scoped.
2. Add standardized CLI outcome/event renderer utilities and exit-code mapping.
3. Align handshake metadata and honestly report unsupported job events/replay.
4. Add profile/auth-source resolution with token redaction.
5. Add instance discovery document writer and `hotrepl discover`.
6. Add `prepare_control`, `hotrepl status`, `hotrepl wait`, and `hotrepl doctor`.
7. Add `hotrepl control run --wait` to supervise jobs on one connection.
8. Add current-connection job events only when a documented client API consumes them; keep
   `jobEventReplaySupported:false` until durable replay exists.
9. Document how game repos compose these primitives into their own live-export commands.

Each step is independently useful and should keep existing eval/control clients working.

## Acceptance criteria

- A coding agent can discover a running HotRepl instance without knowing the port or config path,
  and `discover` does not open a WebSocket.
- A coding agent can run an authenticated mutating control job with one command and no visible
  token.
- Mutating control authority is enforced from the current connection's authenticated session and
  active lease, not by replaying a `leaseId`.
- `hotrepl wait --json` distinguishes unreachable server, auth failure, lease acquisition failure,
  missing command, evaluator failure, blocked/unobserved checks, and timeout.
- Active diagnostic commands report their connection impact, including single-client takeover risk,
  and do not silently acquire the lease.
- Long-running commands can emit parseable progress and exactly one terminal event on the original
  connection.
- The handshake does not advertise capabilities that the client cannot consume.
- CLI output follows stdout/stderr and exit-code conventions suitable for automation.
- Game repos can depend on HotRepl primitives without HotRepl importing game-specific code.

## References

- HotRepl control-plane protocol: `docs/control-plane-protocol.md`
- HotRepl repo guidance and invariants: `AGENTS.md`
- Command Line Interface Guidelines: https://clig.dev/
- Debug Adapter Protocol capability negotiation:
  https://microsoft.github.io/debug-adapter-protocol/overview
- Language Server Protocol capability/versioning model:
  https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/
- Model Context Protocol tool schema/discovery concepts:
  https://modelcontextprotocol.io/specification/2025-06-18/server/tools
- Kubernetes liveness/readiness/startup probe model:
  https://kubernetes.io/docs/concepts/workloads/pods/probes/
- Kubernetes lease coordination concepts: https://kubernetes.io/docs/concepts/architecture/leases/
- OWASP Secrets Management Cheat Sheet:
  https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html
- CWE-214, visible sensitive information in process invocation:
  https://cwe.mitre.org/data/definitions/214.html
