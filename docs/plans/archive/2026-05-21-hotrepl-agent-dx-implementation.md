---
title: "HotRepl Agent DX Implementation Plan"
type: plan
status: implemented
created: 2026-05-21
parent: 2026-05-21-hotrepl-agent-dx-design
superseded_by:
archived: 2026-06-25
---

# HotRepl Agent DX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the reviewed HotRepl Agent DX v1: safe discovery, profile-backed auth,
connection-scoped leases, staged readiness, standardized CLI output, and one-connection job
execution.

**Architecture:** Keep `HotRepl.Core` as the game-agnostic WebSocket/control substrate. Put local
profile/discovery/auth-source resolution and CLI rendering in Python client modules above
`_client.Client`, leaving `_client.py` focused on transport/protocol calls. v1 preserves
same-connection job supervision and explicitly defers replacement-client result recovery and replay.

**Tech Stack:** C# `netstandard2.1` core with Newtonsoft.Json; Python client using `argparse`,
`websockets`, and pytest; docs formatted by dprint.

---

## File Structure

- Modify `src/HotRepl.Core/Control/ControlSessionManager.cs`: enforce one authenticated session per
  connection and validate lease authority against the current connection/session.
- Modify `src/HotRepl.Core/Control/ControlCommandRouter.cs`: require connection id for mutating
  command/job operations and reject operations from non-owning connections.
- Modify `src/HotRepl.Core/ReplEngine.cs`: pass connection ids into control routing, advertise
  accurate control-plane capabilities, and wire instance discovery lifecycle.
- Modify `src/HotRepl.Core/Protocol/Outbound/ControlPlaneHandshake.cs`: add
  `jobEventReplaySupported`.
- Create `src/HotRepl.Core/Discovery/InstanceDocumentWriter.cs`: atomic user-local instance document
  writer with cleanup on dispose.
- Add focused xUnit tests in `tests/HotRepl.Tests/Unit/ControlSessionManagerTests.cs`,
  `ControlRoutingTests.cs`, `MessageSerializerTests.cs`, and a new `InstanceDocumentWriterTests.cs`.
- Create `client/src/hotrepl/_output.py`: shared JSON/JSONL/human outcome renderer and exit-code
  mapping.
- Create `client/src/hotrepl/_profiles.py`: profile loading and auth-source resolution.
- Create `client/src/hotrepl/_discovery.py`: passive instance document discovery and filtering.
- Modify `client/src/hotrepl/_client.py`: add `prepare_control`, control capability helpers, and
  `run_control_job` same-connection supervision.
- Modify `client/src/hotrepl/_types.py`: add typed profile/discovery/readiness/outcome dataclasses
  as needed.
- Modify `client/src/hotrepl/cli.py`: add `discover`, `status`, `wait`, `doctor`, and `control run`;
  route JSON output through `_output.py`.
- Add focused pytest coverage in new/existing `client/tests/test_output.py`, `test_profiles.py`,
  `test_discovery.py`, `test_readiness.py`, and `test_control_run.py`.

---

### Task 1: Connection-scoped control authority

**Files:**

- Modify: `src/HotRepl.Core/Control/ControlSessionManager.cs`
- Modify: `src/HotRepl.Core/Control/ControlCommandRouter.cs`
- Modify: `src/HotRepl.Core/ReplEngine.cs`
- Modify: `src/HotRepl.Core/Protocol/Outbound/ControlPlaneHandshake.cs`
- Test: `tests/HotRepl.Tests/Unit/ControlSessionManagerTests.cs`
- Test: `tests/HotRepl.Tests/Unit/ControlRoutingTests.cs`
- Test: `tests/HotRepl.Tests/Unit/MessageSerializerTests.cs`

- [ ] **Step 1: Write failing tests**

Add tests for:

```csharp
[Fact]
public void Authenticate_RejectsSecondAuthOnSameConnection()
{
    var manager = new ControlSessionManager(new ReplConfig());
    var connection = Guid.NewGuid();
    Assert.True(manager.Authenticate(connection, token: null).Ok);

    var second = manager.Authenticate(connection, token: null);

    Assert.False(second.Ok);
    Assert.Equal("conflict", second.Error!.Kind);
    Assert.Equal("alreadyAuthenticated", second.Error.Code);
}

[Fact]
public void IsLeaseValidForConnection_RejectsLeaseReplayFromDifferentConnection()
{
    var manager = new ControlSessionManager(new ReplConfig { RequireControlLease = true });
    var ownerConnection = Guid.NewGuid();
    var attackerConnection = Guid.NewGuid();
    var owner = manager.Authenticate(ownerConnection, token: null);
    var lease = manager.AcquireLease(ownerConnection, owner.SessionId!, "owner");

    Assert.False(manager.IsLeaseValidForConnection(attackerConnection, lease.LeaseId));
    Assert.True(manager.IsLeaseValidForConnection(ownerConnection, lease.LeaseId));
}
```

Add router tests proving a mutating command with a replayed `leaseId` from another connection
returns `lease_required`, while the owning connection succeeds.

Add handshake test asserting `leaseRequired` follows config and `jobEventReplaySupported`
round-trips as `false`.

- [ ] **Step 2: Run red tests**

Run:

```sh
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter "FullyQualifiedName~ControlSessionManagerTests|FullyQualifiedName~ControlRoutingTests|FullyQualifiedName~MessageSerializerTests"
```

Expected: FAIL because `IsLeaseValidForConnection` and `jobEventReplaySupported` do not exist and
router does not validate by connection.

- [ ] **Step 3: Implement minimal code**

Implement one session per connection,
`AcquireLease(Guid connectionId, string sessionId, string clientName)`,
`IsLeaseValidForConnection(Guid connectionId, string? leaseId)`, and route `CommandCall`,
`JobStatus`, `JobResult`, and `JobCancel` through connection-aware methods. Keep
`sessionId`/`leaseId` in messages for compatibility but never treat them as sufficient authority.
Set `LeaseRequired = _host.Config.RequireControlLease`, `JobEventsSupported = false`, and
`JobEventReplaySupported = false` until current-connection events are implemented.

- [ ] **Step 4: Run green tests**

Run the same filtered `dotnet test` command. Expected: PASS.

- [ ] **Step 5: Commit**

```sh
git add src/HotRepl.Core tests/HotRepl.Tests/Unit
git commit -m "fix: bind control leases to connections"
```

---

### Task 2: Instance discovery document writer

**Files:**

- Create: `src/HotRepl.Core/Discovery/InstanceDocumentWriter.cs`
- Modify: `src/HotRepl.Core/ReplEngine.cs`
- Test: `tests/HotRepl.Tests/Unit/InstanceDocumentWriterTests.cs`

- [ ] **Step 1: Write failing tests**

Add tests that create a temporary discovery directory and assert:

```csharp
[Fact]
public void Write_CreatesDocumentWithoutTokenValue()
```

writes JSON containing `url`, `bindHost`, `port`, `controlPlane.authRequired`,
`controlPlane.leaseRequired`, and `auth.fingerprint`, but not the raw token.

```csharp
[Fact]
public void Dispose_RemovesDocument()
```

asserts the file is deleted on dispose.

- [ ] **Step 2: Run red tests**

Run:

```sh
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~InstanceDocumentWriterTests
```

Expected: FAIL because the writer does not exist.

- [ ] **Step 3: Implement minimal code**

Create `InstanceDocumentWriter` with deterministic injectable root for tests, atomic temp-file write
plus move, no token values, and cleanup on dispose. Integrate it in `ReplEngine.Start()` after the
WebSocket server starts and dispose it in `ReplEngine.Dispose()`.

- [ ] **Step 4: Run green tests**

Run the same filtered test. Expected: PASS.

- [ ] **Step 5: Commit**

```sh
git add src/HotRepl.Core tests/HotRepl.Tests/Unit
git commit -m "feat: write HotRepl instance documents"
```

---

### Task 3: CLI output renderer and exit codes

**Files:**

- Create: `client/src/hotrepl/_output.py`
- Modify: `client/src/hotrepl/cli.py`
- Test: `client/tests/test_output.py`

- [ ] **Step 1: Write failing tests**

Add tests for:

```python
def test_json_success_envelope_contains_schema_command_data_and_meta()

def test_json_error_envelope_goes_to_stderr_and_maps_exit_code()

def test_jsonl_renderer_emits_exactly_one_complete_event()
```

The tests should call renderer functions directly with `io.StringIO` streams and assert
stdout/stderr separation and exit-code mapping for
unreachable/auth/lease/readiness/command/cancel/interrupted/abandoned categories.

- [ ] **Step 2: Run red tests**

Run:

```sh
cd client && uv run pytest tests/test_output.py -q
```

Expected: FAIL because `_output.py` does not exist.

- [ ] **Step 3: Implement minimal code**

Create dataclasses/functions for `CliError`, `json_success`, `json_error`, `jsonl_event`,
`emit_json`, `emit_jsonl`, and `exit_code_for_error`. Keep machine output on stdout for normal
results and stderr for diagnostics/error text.

- [ ] **Step 4: Run green tests**

Run the same pytest command. Expected: PASS.

- [ ] **Step 5: Commit**

```sh
git add client/src/hotrepl/_output.py client/src/hotrepl/cli.py client/tests/test_output.py
git commit -m "feat: add HotRepl CLI output envelopes"
```

---

### Task 4: Passive discovery and profile auth resolution

**Files:**

- Create: `client/src/hotrepl/_discovery.py`
- Create: `client/src/hotrepl/_profiles.py`
- Modify: `client/src/hotrepl/cli.py`
- Test: `client/tests/test_discovery.py`
- Test: `client/tests/test_profiles.py`
- Test: `client/tests/test_control_commands.py`

- [ ] **Step 1: Write failing tests**

Add tests proving:

- `discover_instances(root=tmp_path)` reads candidate JSON and filters by host/profile without
  opening a WebSocket.
- stale invalid JSON is reported as structured diagnostics, not thrown as an uncaught exception.
- `ProfileStore` resolves URL and token sources from env var, token file, and BepInEx config key.
- JSON-safe profile data never includes `auth.token`.
- parser accepts `discover --json`, `--profile`, and `--host`.

- [ ] **Step 2: Run red tests**

Run:

```sh
cd client && uv run pytest tests/test_discovery.py tests/test_profiles.py tests/test_control_commands.py::test_cli_control_subcommands_parse -q
```

Expected: FAIL because modules and parser flags do not exist.

- [ ] **Step 3: Implement minimal code**

Implement filesystem-only discovery roots, profile file loading from explicit `--profile-file` or
default config path, auth token resolution without printing secrets, and CLI `discover` JSON output
through `_output.py`.

- [ ] **Step 4: Run green tests**

Run the same pytest command. Expected: PASS.

- [ ] **Step 5: Commit**

```sh
git add client/src/hotrepl client/tests
git commit -m "feat: add passive discovery and profiles"
```

---

### Task 5: Prepare control, readiness, and same-connection run

**Files:**

- Modify: `client/src/hotrepl/_client.py`
- Modify: `client/src/hotrepl/_types.py`
- Modify: `client/src/hotrepl/cli.py`
- Test: `client/tests/test_readiness.py`
- Test: `client/tests/test_control_run.py`
- Test: `client/tests/_fake_control_server.py`

- [ ] **Step 1: Write failing tests**

Add tests proving:

- `prepare_control(token_source=..., acquire_lease=True, require_commands=[...])` authenticates
  once, acquires lease when requested, and errors if required commands are missing.
- `status --json` reports `connectionImpact.mode == "active-websocket"`,
  `mayReplaceActiveClient == true`, and blocked/unobserved checks after socket/auth failure.
- `wait --lease --commands a,b --json` retries until required commands are visible and acquires the
  lease only after prior checks pass.
- `control run name args --wait --jsonl --lease` authenticates, leases, starts a job, polls
  status/result on the same connection, emits progress events, and emits exactly one terminal event.
- Ctrl-C/cancellation maps confirmed cancellation, mutating interruption, and read-only abandonment
  to the configured exit codes.

- [ ] **Step 2: Run red tests**

Run:

```sh
cd client && uv run pytest tests/test_readiness.py tests/test_control_run.py -q
```

Expected: FAIL because `prepare_control`, readiness commands, and `control run` do not exist.

- [ ] **Step 3: Implement minimal code**

Add high-level client helpers above the existing protocol methods. Implement readiness checks as
data with statuses `pass`, `fail`, `blocked`, and `unobserved`; do not acquire leases for
diagnostics unless requested. Implement `control run` as one process/connection: prepare, call/start
job, poll status until terminal, fetch result, and emit JSONL terminal once. Do not add
reconnect/replay/terminal ack.

- [ ] **Step 4: Run green tests**

Run the same pytest command. Expected: PASS.

- [ ] **Step 5: Commit**

```sh
git add client/src/hotrepl client/tests
git commit -m "feat: add HotRepl readiness and control run"
```

---

### Task 6: Focused integration verification

**Files:**

- Verify all changed files.

- [ ] **Step 1: Run focused C# checks**

```sh
dotnet build src/HotRepl.Core/ --nologo -v q
dotnet test tests/HotRepl.Tests/ --nologo -v q
```

- [ ] **Step 2: Run focused Python checks**

```sh
uvx ruff check
uvx ruff format --check
uvx pyright
cd client && uv run pytest tests/test_output.py tests/test_discovery.py tests/test_profiles.py tests/test_readiness.py tests/test_control_run.py tests/test_control_commands.py tests/test_control_jobs.py -q
```

- [ ] **Step 3: Run docs/repo checks**

```sh
dprint check docs/superpowers/specs/2026-05-21-hotrepl-agent-dx-design.md docs/superpowers/plans/2026-05-21-hotrepl-agent-dx-implementation.md
typos docs/superpowers/specs/2026-05-21-hotrepl-agent-dx-design.md docs/superpowers/plans/2026-05-21-hotrepl-agent-dx-implementation.md
git diff --check
```

- [ ] **Step 4: Fix any failures with TDD**

For each failing behavior, add or adjust the failing test first, run it red, implement the fix, and
rerun the focused command.

- [ ] **Step 5: Final review and commit if needed**

If verification required follow-up edits, commit them atomically with a `fix:` or `test:`
Conventional Commit. Do not push.
