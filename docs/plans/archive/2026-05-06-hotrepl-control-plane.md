---
title: "HotRepl Control Plane Implementation Plan"
type: plan
status: superseded
created: 2026-05-06
parent: 2026-05-06-hotrepl-control-plane-design
superseded_by: 2026-05-22-hotrepl-clean-architecture-implementation
archived: 2026-06-25
---

# HotRepl Control Plane Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use skill://superpowers:subagent-driven-development
> (recommended) or skill://superpowers:executing-plans to implement this plan task-by-task. Steps
> use checkbox (`- [ ]`) syntax for tracking.

**Goal:** HotRepl provides a game-agnostic control plane for typed commands, cooperative jobs,
artifacts, safe local ownership, and reliable automation alongside the eval REPL.

**Architecture:** Core owns protocol DTOs, command registry abstractions, lease/auth/session
ownership, job lifecycle, and artifact metadata. Game hosts optionally register compiled command
handlers; clients use typed control APIs instead of sending ad hoc eval strings for automation.

**Tech Stack:** C# `netstandard2.1`, Newtonsoft.Json, Fleck WebSocket server, HotRepl Python
client/CLI, xUnit C# tests, pytest client tests.

---

## Reference documents

- Spec: `docs/superpowers/specs/2026-05-06-hotrepl-control-plane-design.md`
- Repo guidance: `AGENTS.md`
- Protocol implementation: `src/HotRepl.Core/Protocol/Messages.cs`
- Routing implementation: `src/HotRepl.Core/Server/MessageRouter.cs`
- Engine implementation: `src/HotRepl.Core/ReplEngine.cs`
- Client implementation: `client/src/hotrepl/_client.py`, `client/src/hotrepl/_types.py`,
  `client/src/hotrepl/cli.py`

## File structure

New C# files:

- `src/HotRepl.Core/Control/ControlCommandDescriptor.cs` — command metadata advertised to clients.
- `src/HotRepl.Core/Control/ControlCommandError.cs` — stable command/job error envelope.
- `src/HotRepl.Core/Control/ControlCommandResult.cs` — command handler return envelope.
- `src/HotRepl.Core/Control/ControlCommandContext.cs` — execution context passed to handlers.
- `src/HotRepl.Core/Control/IControlCommandHandler.cs` — game-agnostic handler interface.
- `src/HotRepl.Core/Control/IControlCommandRegistry.cs` — registry interface exposed by hosts.
- `src/HotRepl.Core/Control/EmptyControlCommandRegistry.cs` — default registry when no host commands
  exist.
- `src/HotRepl.Core/Control/ControlSessionManager.cs` — auth + exclusive lease ownership.
- `src/HotRepl.Core/Control/ControlCommandRouter.cs` — validates and enqueues command calls.
- `src/HotRepl.Core/Control/Jobs/ControlJob.cs` — durable in-memory job state.
- `src/HotRepl.Core/Control/Jobs/ControlJobManager.cs` — job lifecycle, event buffer, cancellation.
- `src/HotRepl.Core/Control/Jobs/ControlJobEvent.cs` — progress/event DTO.
- `src/HotRepl.Core/Control/Artifacts/ArtifactRef.cs` — logical artifact metadata.

Modified C# files:

- `src/HotRepl.Core/IReplHost.cs` — optional command registry access.
- `src/HotRepl.Core/ReplConfig.cs` — control-plane config: bind host, auth, queue/job limits.
- `src/HotRepl.Core/HostInfo.cs` — optional control capabilities if needed for handshake metadata.
- `src/HotRepl.Core/Protocol/Messages.cs` — new inbound/outbound protocol records.
- `src/HotRepl.Core/Protocol/MessageSerializer.cs` — message type mapping.
- `src/HotRepl.Core/Server/MessageRouter.cs` — route control messages.
- `src/HotRepl.Core/Server/ReplWebSocketServer.cs` — loopback default and auth/lease wiring.
- `src/HotRepl.Core/Server/ClientRegistry.cs` — no fallback-to-current-client for addressed control
  responses.
- `src/HotRepl.Core/ReplEngine.cs` — main-thread execution for control commands/jobs and handshake
  capabilities.

Modified Python files:

- `client/src/hotrepl/_types.py` — control-plane dataclasses/types.
- `client/src/hotrepl/_client.py` — `auth`, `acquire_lease`, `describe_commands`, `call`,
  `start_job`, `job_status`, `job_result`, `cancel_job`.
- `client/src/hotrepl/cli.py` — `hotrepl control ...` subcommands.

New/modified tests:

- `tests/HotRepl.Tests/Unit/ControlMessageSerializerTests.cs`
- `tests/HotRepl.Tests/Unit/ControlSessionManagerTests.cs`
- `tests/HotRepl.Tests/Unit/ControlCommandRegistryTests.cs`
- `tests/HotRepl.Tests/Unit/ControlJobManagerTests.cs`
- `tests/HotRepl.Tests/Unit/ControlRoutingTests.cs`
- `client/tests/test_control_handshake.py`
- `client/tests/test_control_commands.py`
- `client/tests/test_control_jobs.py`
- `client/tests/test_control_errors.py`

---

## Phase 1: Protocol DTOs and serialization

### Task 1: Add control-plane message DTOs

**Files:**

- Modify: `src/HotRepl.Core/Protocol/Messages.cs`
- Test: `tests/HotRepl.Tests/Unit/ControlMessageSerializerTests.cs`

- [x] **Step 1: Write serializer tests for new message types**

Add tests covering these message names exactly:

```text
control_auth
control_auth_result
lease_acquire
lease_acquire_result
command_describe
command_describe_result
command_call
command_result
command_error
command_accepted
job_status
job_status_result
job_result
job_cancel
job_cancel_result
job_event
```

Each test asserts round-trip serialization preserves `type`, `id`, and the command/job-specific
fields.

Run:

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter ControlMessageSerializerTests
```

Expected: fail because DTOs and serializer mappings do not exist.

- [x] **Step 2: Add DTO records and message type constants**

Add records in `Messages.cs` matching the spec. Required fields:

```csharp
public static class MessageTypes
{
    public const string ControlAuth = "control_auth";
    public const string ControlAuthResult = "control_auth_result";
    public const string LeaseAcquire = "lease_acquire";
    public const string LeaseAcquireResult = "lease_acquire_result";
    public const string CommandDescribe = "command_describe";
    public const string CommandDescribeResult = "command_describe_result";
    public const string CommandCall = "command_call";
    public const string CommandResult = "command_result";
    public const string CommandError = "command_error";
    public const string CommandAccepted = "command_accepted";
    public const string JobStatus = "job_status";
    public const string JobStatusResult = "job_status_result";
    public const string JobResult = "job_result";
    public const string JobCancel = "job_cancel";
    public const string JobCancelResult = "job_cancel_result";
    public const string JobEvent = "job_event";
}
```

Use `Newtonsoft.Json.Linq.JObject` for command args/results so HotRepl core stays schema-neutral.

- [x] **Step 3: Map new messages in `MessageSerializer`**

Route inbound messages by `type` to their DTO classes. Unknown messages keep compatibility behavior
for clients that use other message types.

- [x] **Step 4: Verify tests pass**

Run:

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter ControlMessageSerializerTests
```

Expected: pass.

- [x] **Step 5: Commit**

```bash
git add src/HotRepl.Core/Protocol/Messages.cs src/HotRepl.Core/Protocol/MessageSerializer.cs tests/HotRepl.Tests/Unit/ControlMessageSerializerTests.cs
git commit -m "feat(protocol): add control-plane message contracts"
```

### Task 2: Advertise control capabilities in handshake

**Files:**

- Modify: `src/HotRepl.Core/Protocol/Messages.cs`
- Modify: `src/HotRepl.Core/ReplEngine.cs`
- Modify: `src/HotRepl.Core/ReplConfig.cs`
- Test: `tests/HotRepl.Tests/Unit/MessageSerializerTests.cs`
- Test: `client/tests/test_control_handshake.py`

- [x] **Step 1: Add failing handshake tests**

C# test asserts serialized handshake includes optional `controlPlane` object when enabled.

Python test asserts `Client.connect()` exposes `handshake.control_plane.supported` without breaking
handshake fields.

- [x] **Step 2: Add config properties**

Add to `ReplConfig`:

```csharp
public bool ControlPlaneEnabled { get; set; } = true;
public string BindHost { get; set; } = "127.0.0.1";
public bool RequireControlAuth { get; set; } = false;
public int MaxControlMessageBytes { get; set; } = 1024 * 1024;
public int MaxQueuedControlCommands { get; set; } = 32;
public int MaxJobEventBuffer { get; set; } = 1000;
```

Keep port behavior stable while server startup uses the configured bind host.

- [x] **Step 3: Extend handshake DTO**

Add `ControlPlaneHandshake? ControlPlane` with fields from the spec.

- [x] **Step 4: Populate handshake in `ReplEngine`**

When `ControlPlaneEnabled` is true, include protocol version `1`, artifact/job support flags, auth
mode, and limits.

- [x] **Step 5: Update Python types**

Add optional `ControlPlaneInfo` to `client/src/hotrepl/_types.py` and parse it in `_client.py`
handshake logic.

- [x] **Step 6: Run tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter MessageSerializerTests
cd client && uv run pytest tests/test_control_handshake.py -q
```

Expected: both pass.

- [x] **Step 7: Commit**

```bash
git add src/HotRepl.Core/Protocol/Messages.cs src/HotRepl.Core/ReplConfig.cs src/HotRepl.Core/ReplEngine.cs tests/HotRepl.Tests/Unit/MessageSerializerTests.cs client/src/hotrepl/_types.py client/src/hotrepl/_client.py client/tests/test_control_handshake.py
git commit -m "feat(protocol): advertise control-plane capabilities"
```

---

## Phase 2: Command registry and synchronous calls

### Task 3: Add command registry abstractions

**Files:**

- Create: `src/HotRepl.Core/Control/ControlCommandDescriptor.cs`
- Create: `src/HotRepl.Core/Control/ControlCommandError.cs`
- Create: `src/HotRepl.Core/Control/ControlCommandResult.cs`
- Create: `src/HotRepl.Core/Control/ControlCommandContext.cs`
- Create: `src/HotRepl.Core/Control/IControlCommandHandler.cs`
- Create: `src/HotRepl.Core/Control/IControlCommandRegistry.cs`
- Create: `src/HotRepl.Core/Control/EmptyControlCommandRegistry.cs`
- Modify: `src/HotRepl.Core/IReplHost.cs`
- Test: `tests/HotRepl.Tests/Unit/ControlCommandRegistryTests.cs`

- [x] **Step 1: Write failing registry tests**

Tests:

1. Empty registry describes zero commands.
2. Descriptor requires non-empty command name and positive version.
3. Host without explicit registry returns the empty registry.

- [x] **Step 2: Add control abstractions**

Define:

```csharp
public enum ControlCommandKind { Synchronous, Job }

public sealed record ControlCommandDescriptor(
    string Name,
    int Version,
    ControlCommandKind Kind,
    bool MutatesState,
    JObject ArgsSchema,
    JObject ResultSchema);

public sealed record ControlCommandError(
    string Kind,
    string Code,
    string Message,
    bool Retryable,
    JObject? Details = null);

public sealed record ArtifactRef(
    string LogicalName,
    string Uri,
    string? Path,
    string ContentType,
    long ByteSize,
    string Sha256,
    bool Finalized);

public sealed record ControlCommandResult(
    JObject Result,
    IReadOnlyList<ArtifactRef> Artifacts,
    IReadOnlyList<ControlCommandError> Diagnostics);
```

Place `ArtifactRef` in `Control/Artifacts/ArtifactRef.cs` but reference it from
`ControlCommandResult`.

- [x] **Step 3: Extend host boundary**

Add to `IReplHost`:

```csharp
IControlCommandRegistry ControlCommands { get; }
```

Host adapters return `EmptyControlCommandRegistry.Instance` unless they register commands.

- [x] **Step 4: Run registry tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter ControlCommandRegistryTests
```

Expected: pass.

- [x] **Step 5: Commit**

```bash
git add src/HotRepl.Core/Control src/HotRepl.Core/IReplHost.cs src/HotRepl.BepInEx src/HotRepl.Host.MelonLoader tests/HotRepl.Tests/Unit/ControlCommandRegistryTests.cs
git commit -m "feat(control): add command registry abstractions"
```

### Task 4: Route `command_describe` and `command_call`

**Files:**

- Create: `src/HotRepl.Core/Control/ControlCommandRouter.cs`
- Modify: `src/HotRepl.Core/Server/MessageRouter.cs`
- Modify: `src/HotRepl.Core/ReplEngine.cs`
- Test: `tests/HotRepl.Tests/Unit/ControlRoutingTests.cs`

- [x] **Step 1: Write failing routing tests**

Tests:

1. `command_describe` returns descriptors from a fake host registry.
2. Unknown command returns `command_error` with `kind = unknown_command`.
3. Known synchronous command executes on engine tick and returns `command_result`.
4. Handler exception returns `command_error` with `kind = internal` and `retryable = false`.

- [x] **Step 2: Add `ControlCommandRouter`**

The router validates command name, looks up handler, and converts handler output/errors into
protocol DTOs. It must not execute handlers from socket threads.

- [x] **Step 3: Wire router through `MessageRouter` and `ReplEngine`**

Inbound control messages enqueue engine commands. Tick order is: cancel drain, command queue,
accepted control job start, at most one eval, subscriptions. Control command execution lives in the
command queue portion.

- [x] **Step 4: Run routing tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter ControlRoutingTests
```

Expected: pass.

- [x] **Step 5: Commit**

```bash
git add src/HotRepl.Core/Control/ControlCommandRouter.cs src/HotRepl.Core/Server/MessageRouter.cs src/HotRepl.Core/ReplEngine.cs tests/HotRepl.Tests/Unit/ControlRoutingTests.cs
git commit -m "feat(control): execute typed synchronous commands"
```

---

## Phase 3: Session safety

### Task 5: Add auth and exclusive lease manager

**Files:**

- Create: `src/HotRepl.Core/Control/ControlSessionManager.cs`
- Modify: `src/HotRepl.Core/ReplConfig.cs`
- Modify: `src/HotRepl.Core/Server/MessageRouter.cs`
- Test: `tests/HotRepl.Tests/Unit/ControlSessionManagerTests.cs`

- [x] **Step 1: Write failing session tests**

Tests:

1. Auth succeeds when no token is required.
2. Auth fails with `auth_failed` for wrong token when token is configured.
3. Lease acquisition succeeds for authenticated session.
4. Second lease acquisition fails with `lease_conflict` while first lease is active.
5. Mutating command without lease fails with `lease_required`.

- [x] **Step 2: Implement `ControlSessionManager`**

Track:

```text
sessionId
client connection id
leaseId
lease holder
createdAt
lastSeenAt
```

Leases are in-memory and do not persist across process restart.

- [x] **Step 3: Add config**

Add:

```csharp
public string? ControlAuthToken { get; set; }
public bool RequireControlLease { get; set; } = true;
```

- [x] **Step 4: Gate mutating commands**

If command descriptor `MutatesState` is true, require valid lease. Read-only commands may run after
auth without lease.

- [x] **Step 5: Run tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter ControlSessionManagerTests
```

Expected: pass.

- [x] **Step 6: Commit**

```bash
git add src/HotRepl.Core/Control/ControlSessionManager.cs src/HotRepl.Core/ReplConfig.cs src/HotRepl.Core/Server/MessageRouter.cs tests/HotRepl.Tests/Unit/ControlSessionManagerTests.cs
git commit -m "feat(control): require authenticated command leases"
```

### Task 6: Bind loopback by default and remove control response fallback

**Files:**

- Modify: `src/HotRepl.Core/Server/ReplWebSocketServer.cs`
- Modify: `src/HotRepl.Core/Server/ClientRegistry.cs`
- Test: `tests/HotRepl.Tests/Unit/ControlSessionManagerTests.cs`

- [x] **Step 1: Add failing tests**

Tests:

1. Default server URL uses `127.0.0.1`.
2. Explicit bind host `0.0.0.0` is honored only when configured.
3. Addressed control responses to a disconnected client are dropped or recorded as undeliverable;
   they must not be sent to the replacement client.

- [x] **Step 2: Implement bind host config in server startup**

Use `ReplConfig.BindHost` when constructing the Fleck URL.

- [x] **Step 3: Split eval fallback from control response delivery**

Eval responses use REPL compatibility delivery when needed. Control responses require the original
connection/session.

- [x] **Step 4: Run tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter ControlSessionManagerTests
```

Expected: pass.

- [x] **Step 5: Commit**

```bash
git add src/HotRepl.Core/Server/ReplWebSocketServer.cs src/HotRepl.Core/Server/ClientRegistry.cs tests/HotRepl.Tests/Unit/ControlSessionManagerTests.cs
git commit -m "fix(server): secure control-plane session ownership"
```

---

## Phase 4: Jobs and artifacts

### Task 7: Add job lifecycle manager

**Files:**

- Create: `src/HotRepl.Core/Control/Jobs/ControlJob.cs`
- Create: `src/HotRepl.Core/Control/Jobs/ControlJobEvent.cs`
- Create: `src/HotRepl.Core/Control/Jobs/ControlJobManager.cs`
- Test: `tests/HotRepl.Tests/Unit/ControlJobManagerTests.cs`

- [x] **Step 1: Write failing job tests**

Tests:

1. Starting a job creates state `accepted`.
2. Running job transitions to `running` then `completed` with result.
3. Failed handler transitions to `failed` with structured error.
4. Cancellation transitions through `cancelling` to `cancelled` for cooperative handler.
5. Event buffer returns events after requested sequence.
6. Event buffer caps at `MaxJobEventBuffer`.

- [x] **Step 2: Implement job models**

States are exact strings on the wire:

```text
accepted
running
completed
failed
cancelling
cancelled
```

Internally use enum if desired, but protocol output must use these strings.

- [x] **Step 3: Implement cooperative cancellation**

Each job owns a `CancellationTokenSource`. `job_cancel` cancels the token and returns
`job_cancel_result` immediately with whether cancellation was accepted.

- [x] **Step 4: Run job tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter ControlJobManagerTests
```

Expected: pass.

- [x] **Step 5: Commit**

```bash
git add src/HotRepl.Core/Control/Jobs tests/HotRepl.Tests/Unit/ControlJobManagerTests.cs
git commit -m "feat(control): add cooperative job lifecycle"
```

### Task 8: Route job protocol messages

**Files:**

- Modify: `src/HotRepl.Core/Control/ControlCommandRouter.cs`
- Modify: `src/HotRepl.Core/ReplEngine.cs`
- Modify: `src/HotRepl.Core/Server/MessageRouter.cs`
- Test: `tests/HotRepl.Tests/Unit/ControlRoutingTests.cs`

- [x] **Step 1: Add failing routing tests**

Tests:

1. Job command returns `command_accepted`.
2. `job_status` returns state.
3. `job_result` before terminal state returns `conflict` or `busy`.
4. `job_result` after completion returns artifacts and result.
5. `job_cancel` returns acknowledgement.

- [x] **Step 2: Route job command handlers through `ControlJobManager`**

Descriptors with `Kind = Job` must not block `command_call` until completion.

- [x] **Step 3: Add artifact refs to result DTOs**

Ensure `ArtifactRef` serializes with `logicalName`, `uri`, `path`, `contentType`, `byteSize`,
`sha256`, and `finalized`.

- [x] **Step 4: Run tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter ControlRoutingTests
```

Expected: pass.

- [x] **Step 5: Commit**

```bash
git add src/HotRepl.Core/Control src/HotRepl.Core/ReplEngine.cs src/HotRepl.Core/Server/MessageRouter.cs tests/HotRepl.Tests/Unit/ControlRoutingTests.cs
git commit -m "feat(control): expose job status results and artifacts"
```

---

## Phase 5: Python client and CLI

### Task 9: Add Python control client API

**Files:**

- Modify: `client/src/hotrepl/_types.py`
- Modify: `client/src/hotrepl/_client.py`
- Test: `client/tests/test_control_commands.py`
- Test: `client/tests/test_control_jobs.py`
- Test: `client/tests/test_control_errors.py`

- [x] **Step 1: Write failing Python tests with fake WebSocket server fixtures**

Tests cover:

1. `describe_commands()` sends `command_describe` and parses descriptors.
2. `call()` sends `command_call` and returns parsed result object.
3. `call()` raises a typed exception for `command_error`.
4. `start_job()` returns `jobId` from `command_accepted`.
5. `job_status()`, `job_result()`, and `cancel_job()` parse their responses.

- [x] **Step 2: Add Python dataclasses/types**

Types:

```python
ControlCommandDescriptor
ControlError
ArtifactRef
CommandResult
CommandAccepted
JobStatus
```

- [x] **Step 3: Implement client methods**

Methods:

```python
async def authenticate(self, token: str | None = None) -> AuthResult
async def acquire_lease(self, client_name: str) -> LeaseResult
async def describe_commands(self) -> list[ControlCommandDescriptor]
async def call(self, name: str, args: dict, *, timeout_ms: int | None = None, idempotency_key: str | None = None) -> CommandResult
async def start_job(self, name: str, args: dict, *, timeout_ms: int | None = None, idempotency_key: str | None = None) -> CommandAccepted
async def job_status(self, job_id: str) -> JobStatus
async def job_result(self, job_id: str) -> CommandResult
async def cancel_job(self, job_id: str) -> JobCancelResult
```

- [x] **Step 4: Run Python tests**

```bash
cd client
uv run pytest tests/test_control_commands.py tests/test_control_jobs.py tests/test_control_errors.py -q
```

Expected: pass.

- [x] **Step 5: Commit**

```bash
git add client/src/hotrepl/_types.py client/src/hotrepl/_client.py client/tests/test_control_commands.py client/tests/test_control_jobs.py client/tests/test_control_errors.py
git commit -m "feat(client): add control-plane API"
```

### Task 10: Add generic CLI control commands

**Files:**

- Modify: `client/src/hotrepl/cli.py`
- Test: `client/tests/test_control_commands.py`

- [x] **Step 1: Write failing CLI tests**

Tests cover argument parsing for:

```text
hotrepl control describe
hotrepl control call archive.preflight '{}'
hotrepl control job-status job-123
hotrepl control job-result job-123
hotrepl control cancel job-123
```

- [x] **Step 2: Implement CLI commands**

CLI outputs JSON to stdout for machine use. Do not add non-JSON decoration to control command
output.

- [x] **Step 3: Run tests**

```bash
cd client
uv run pytest tests/test_control_commands.py -q
```

Expected: pass.

- [x] **Step 4: Commit**

```bash
git add client/src/hotrepl/cli.py client/tests/test_control_commands.py
git commit -m "feat(cli): expose control-plane commands"
```

---

## Phase 6: Documentation and gates

### Task 11: Document control-plane contracts

**Files:**

- Modify: `README.md`
- Modify: `AGENTS.md`
- Create: `docs/control-plane-protocol.md`

- [x] **Step 1: Add protocol documentation**

Document:

- handshake fields;
- auth and lease flow;
- command registry;
- sync command flow;
- job command flow;
- cancellation semantics;
- artifact refs;
- error kinds;
- compatibility with eval.

- [x] **Step 2: Update `AGENTS.md`**

Add invariants:

- Core remains game-agnostic.
- Control handlers execute on main-thread tick path only.
- Mutating commands require lease.
- Control responses must not be delivered to replacement clients.
- Artifacts are metadata references, not bulk payloads.

- [x] **Step 3: Commit**

```bash
git add README.md AGENTS.md docs/control-plane-protocol.md
git commit -m "docs(control): describe control-plane protocol"
```

### Task 12: Run final verification

**Files:** all touched files.

- [x] **Step 1: Run C# gates**

```bash
dotnet build src/HotRepl.Core/ --nologo -v q
dotnet test tests/HotRepl.Tests/ --nologo -v q
dotnet format src/HotRepl.Core/ --verify-no-changes
```

Expected: exit 0 for all.

- [x] **Step 2: Run Python client tests**

```bash
cd client
uv run pytest tests -q
```

Expected: exit 0.

- [x] **Step 3: Commit any test/doc fixes**

Only commit if Step 1 or Step 2 required fixes.

```bash
git status --short
git add <fixed-files>
git commit -m "fix(control): complete verification fixes"
```

Expected final state: no uncommitted changes.

---

## Ardenfall integration scope

Ardenfall Archives integration is a separate implementation plan:

1. register Ardenfall export commands in a compiled BepInEx mod;
2. replace the F8-only monolith with a shared `ExtractionService`;
3. add a controller CLI that deploys, launches, connects, exports, validates, and runs the pipeline;
4. tighten snapshot manifest validation in the pipeline.

Ardenfall command implementation depends on the HotRepl control-plane protocol and Python client.

## Self-review

- Spec coverage: command registry, auth/lease, jobs, artifact refs, client API, CLI, docs, and gates
  are covered.
- Placeholder scan: no `TBD`/`TODO` placeholders remain; Ardenfall integration is scoped separately.
- Type consistency: protocol names match the design spec; command/job/error/artifact names are
  consistent across C# and Python tasks.
- Scope: this plan intentionally covers HotRepl only. Ardenfall integration is scoped separately.

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-06-hotrepl-control-plane.md`. Two
execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks,
fast iteration.

**2. Inline Execution** — execute tasks in this session using executing-plans, batch execution with
checkpoints.
