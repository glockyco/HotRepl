"""Async WebSocket client for the HotRepl C# REPL protocol."""

from __future__ import annotations

import asyncio
import contextlib
import json
import logging
import time
from typing import TYPE_CHECKING, Any, Protocol, cast

if TYPE_CHECKING:
    from collections.abc import AsyncGenerator, Callable, Sequence
    from types import TracebackType
    from typing import Self

import websockets

from hotrepl._types import (
    ArtifactRef,
    AuthResult,
    CommandAccepted,
    CommandResult,
    ControlCommandDescriptor,
    ControlError,
    ControlRunTerminal,
    JobCancelResult,
    JobStatus,
    LeaseResult,
    PreparedControl,
)

DEFAULT_URL = "ws://localhost:18590"
CLIENT_TIMEOUT_S = 30.0  # Hard ceiling on any single round-trip

_log = logging.getLogger("hotrepl.client")

_TERMINAL_JOB_STATES = {"completed", "failed", "cancelled"}


class TokenSource(Protocol):
    """Object that resolves a control auth token."""

    def resolve_token(self) -> str | None: ...


class EvalError(Exception):
    """Raised when the server returns an eval_error response."""

    def __init__(self, message: str, kind: str, stack_trace: str | None = None) -> None:
        self.kind = kind
        self.stack_trace = stack_trace
        super().__init__(message)


class ServerUnreachableError(Exception):
    """Cannot reach the HotRepl server."""


class ControlCommandError(Exception):
    """Raised when the server returns a command_error response."""

    def __init__(self, error: ControlError, diagnostics: list[ControlError] | None = None) -> None:
        self.error = error
        self.diagnostics = diagnostics or []
        super().__init__(error.message)


class Client:
    """Async WebSocket client for HotRepl eval requests."""

    def __init__(self, url: str = DEFAULT_URL) -> None:
        self.url = url
        self._ws: websockets.ClientConnection | None = None
        self._counter = 0
        self.handshake: dict[str, Any] | None = None
        self.session_id: str | None = None
        self.lease_id: str | None = None

    # -- context manager --

    async def __aenter__(self) -> Self:
        await self.connect()
        return self

    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> None:
        await self.close()

    # -- lifecycle --

    async def connect(self) -> dict[str, Any]:
        """Open the WebSocket and consume the server handshake message."""
        try:
            self._ws = await asyncio.wait_for(
                websockets.connect(self.url),
                timeout=CLIENT_TIMEOUT_S,
            )
        except (OSError, ConnectionRefusedError) as exc:
            raise ServerUnreachableError("Game not running or HotRepl not loaded") from exc

        ws = self._require_ws()
        raw = await asyncio.wait_for(ws.recv(), timeout=CLIENT_TIMEOUT_S)
        handshake: dict[str, Any] = json.loads(raw)
        self.handshake = handshake
        return handshake

    async def close(self) -> None:
        if self._ws is not None:
            await self._ws.close()
            self._ws = None

    def _require_ws(self) -> websockets.ClientConnection:
        """Return the live websocket or raise if connect() was not called."""
        if self._ws is None:
            raise RuntimeError("Client is not connected; call connect() first.")
        return self._ws

    # -- commands --

    async def eval(self, code: str, timeout_ms: int = 10000) -> dict[str, Any]:
        """Send code for evaluation; returns the full response dict.

        Raises EvalError on eval_error responses and asyncio.TimeoutError
        if no response arrives within *timeout_ms* + a client-side margin.
        """
        msg_id = self._next_id()
        payload = {
            "type": "eval",
            "id": msg_id,
            "code": code,
            "timeoutMs": timeout_ms,
        }
        # Client-side timeout: server timeout + 2 s margin
        client_timeout = (timeout_ms / 1000) + 2.0
        return await self._request(payload, msg_id, budget_s=client_timeout)

    async def reset(self) -> dict[str, Any]:
        """Reset the server-side REPL state."""
        msg_id = self._next_id()
        payload = {"type": "reset", "id": msg_id}
        return await self._request(payload, msg_id)

    async def ping(self) -> float:
        """Ping the server; returns round-trip time in milliseconds."""
        msg_id = self._next_id()
        payload = {"type": "ping", "id": msg_id}
        t0 = time.perf_counter()
        await self._request(payload, msg_id)
        return (time.perf_counter() - t0) * 1000

    async def cancel(self, eval_id: str) -> None:
        """Cancel a running eval or subscription by its id.

        Fire-and-forget — the cancel protocol uses the target's id as the
        message id and no response is expected.
        """
        payload = {"type": "cancel", "id": eval_id}
        ws = self._require_ws()
        await ws.send(json.dumps(payload))

    async def select_evaluator(self, evaluator: str) -> dict[str, Any]:
        """Select a server-side evaluator by advertised capability name."""
        msg_id = self._next_id()
        payload = {"type": "select_evaluator", "id": msg_id, "evaluator": evaluator}
        return await self._request(payload, msg_id)

    async def authenticate(self, token: str | None = None) -> AuthResult:
        msg_id = self._next_id()
        payload: dict[str, Any] = {"type": "control_auth", "id": msg_id}
        if token is not None:
            payload["token"] = token
        resp = await self._request(payload, msg_id)
        result = AuthResult(
            ok=bool(resp.get("ok")),
            session_id=resp.get("sessionId"),
            error=_parse_control_error(resp.get("error")) if resp.get("error") else None,
        )
        if result.ok:
            self.session_id = result.session_id
        return result

    async def acquire_lease(self, client_name: str) -> LeaseResult:
        if self.session_id is None:
            await self.authenticate()
        if self.session_id is None:
            raise RuntimeError("Authentication did not produce a session id.")
        msg_id = self._next_id()
        payload = {
            "type": "lease_acquire",
            "id": msg_id,
            "sessionId": self.session_id,
            "clientName": client_name,
        }
        resp = await self._request(payload, msg_id)
        result = LeaseResult(
            ok=bool(resp.get("ok")),
            lease_id=resp.get("leaseId"),
            error=_parse_control_error(resp.get("error")) if resp.get("error") else None,
        )
        if result.ok:
            self.lease_id = result.lease_id
        return result

    async def prepare_control(
        self,
        *,
        token_source: str | TokenSource | None = None,
        client_name: str = "hotrepl-cli",
        acquire_lease: bool = False,
        require_commands: Sequence[str] | None = None,
    ) -> PreparedControl:
        token = _resolve_token_source(token_source)
        auth: AuthResult | None = None
        control_plane = cast("dict[str, Any]", (self.handshake or {}).get("controlPlane") or {})
        if bool(control_plane.get("authRequired")) or token is not None:
            auth = await self.authenticate(token)
            if not auth.ok:
                raise ControlCommandError(auth.error or _control_error("auth_failed", "authFailed"))

        commands = await self.describe_commands()
        missing = _missing_commands(commands, require_commands)
        if missing:
            raise ControlCommandError(
                ControlError(
                    "validation_failed",
                    "missingCommands",
                    "Missing required control commands: " + ", ".join(missing),
                    True,
                    {"missing": missing},
                )
            )

        lease: LeaseResult | None = None
        if acquire_lease:
            lease = await self.acquire_lease(client_name)
            if not lease.ok:
                raise ControlCommandError(
                    lease.error or _control_error("lease_conflict", "leaseAcquisitionFailed")
                )

        return PreparedControl(auth=auth, lease=lease, commands=commands)

    async def run_control_job(
        self,
        name: str,
        args: dict[str, Any],
        *,
        wait: bool,
        poll_interval_s: float = 0.5,
        interrupt_grace_s: float = 5.0,
        mutates_state: bool = False,
        timeout_ms: int | None = None,
        idempotency_key: str | None = None,
        on_progress: Callable[[JobStatus], None] | None = None,
    ) -> ControlRunTerminal:
        accepted = await self.start_job(
            name,
            args,
            timeout_ms=timeout_ms,
            idempotency_key=idempotency_key,
        )
        if not wait:
            return ControlRunTerminal(status=accepted.state)

        try:
            return await self._wait_for_job_terminal(
                accepted.job_id,
                poll_interval_s=poll_interval_s,
                timeout_s=None,
                on_progress=on_progress,
            )
        except asyncio.CancelledError:
            with contextlib.suppress(Exception):
                await self.cancel_job(accepted.job_id)
            try:
                return await self._wait_for_job_terminal(
                    accepted.job_id,
                    poll_interval_s=min(poll_interval_s, 0.05),
                    timeout_s=interrupt_grace_s,
                    on_progress=on_progress,
                )
            except TimeoutError:
                return _unconfirmed_interrupt_terminal(mutates_state)

    async def describe_commands(self) -> list[ControlCommandDescriptor]:
        msg_id = self._next_id()
        resp = await self._request({"type": "command_describe", "id": msg_id}, msg_id)
        return [_parse_descriptor(item) for item in resp.get("commands", [])]

    async def call(
        self,
        name: str,
        args: dict[str, Any],
        *,
        timeout_ms: int | None = None,
        idempotency_key: str | None = None,
    ) -> CommandResult:
        msg_id = self._next_id()
        payload = self._command_payload(msg_id, name, args, timeout_ms, idempotency_key)
        resp = await self._request(payload, msg_id)
        if resp.get("type") != "command_result":
            raise RuntimeError(f"Expected command_result, got {resp.get('type')}")
        return _parse_command_result(resp)

    async def start_job(
        self,
        name: str,
        args: dict[str, Any],
        *,
        timeout_ms: int | None = None,
        idempotency_key: str | None = None,
    ) -> CommandAccepted:
        msg_id = self._next_id()
        payload = self._command_payload(msg_id, name, args, timeout_ms, idempotency_key)
        resp = await self._request(payload, msg_id)
        if resp.get("type") != "command_accepted":
            raise RuntimeError(f"Expected command_accepted, got {resp.get('type')}")
        return CommandAccepted(job_id=resp["jobId"], state=resp["state"])

    async def job_status(self, job_id: str) -> JobStatus:
        msg_id = self._next_id()
        payload = self._job_payload("job_status", msg_id, job_id)
        resp = await self._request(payload, msg_id)
        return JobStatus(job_id=resp["jobId"], state=resp["state"], progress=resp.get("progress"))

    async def job_result(self, job_id: str) -> CommandResult:
        msg_id = self._next_id()
        payload = self._job_payload("job_result", msg_id, job_id)
        resp = await self._request(payload, msg_id)
        if resp.get("type") != "job_result":
            raise RuntimeError(f"Expected job_result, got {resp.get('type')}")
        return _parse_command_result(resp)

    async def cancel_job(self, job_id: str) -> JobCancelResult:
        msg_id = self._next_id()
        payload = self._job_payload("job_cancel", msg_id, job_id)
        resp = await self._request(payload, msg_id)
        return JobCancelResult(accepted=bool(resp.get("accepted")), state=resp["state"])

    async def complete(self, code: str, cursor_pos: int = -1) -> list[str]:
        """Request autocomplete suggestions for *code* at *cursor_pos*.

        Returns a list of completion strings. Does not execute code.
        ``cursor_pos=-1`` means end of code (server default).
        """
        msg_id = self._next_id()
        payload: dict[str, Any] = {
            "type": "complete",
            "id": msg_id,
            "code": code,
            "cursorPos": cursor_pos,
        }
        resp = await self._request(payload, msg_id)
        completions: list[str] = resp.get("completions", [])
        return completions

    async def subscribe(
        self,
        code: str,
        *,
        interval_frames: int = 1,
        on_change: bool = False,
        limit: int = 0,
        timeout_ms: int = 10000,
    ) -> AsyncGenerator[dict[str, Any], None]:
        """Subscribe to repeated evaluation of *code*.

        Yields dicts with keys: seq, hasValue, value, valueType, durationMs, final.
        On error yields: seq, errorKind, message, final.
        Stops when the server sends ``final: true`` or the generator is closed.
        """
        msg_id = self._next_id()
        payload: dict[str, Any] = {
            "type": "subscribe",
            "id": msg_id,
            "code": code,
            "intervalFrames": interval_frames,
            "onChange": on_change,
            "limit": limit,
            "timeoutMs": timeout_ms,
        }
        ws = self._require_ws()
        await ws.send(json.dumps(payload))

        try:
            while True:
                raw = await asyncio.wait_for(ws.recv(), timeout=CLIENT_TIMEOUT_S)
                resp: dict[str, Any] = json.loads(raw)

                # Unsolicited server notification — log and keep waiting.
                if resp.get("type") == "assembly_reload":
                    asm = resp.get("assembly") or "unknown"
                    _log.warning("Assembly reloaded: %s. REPL session reset.", asm)
                    continue

                if resp.get("id") != msg_id:
                    continue  # Not ours; skip.

                yield resp

                if resp.get("final", False):
                    return
        except GeneratorExit:
            # Generator was closed (e.g. Ctrl-C) — cancel the subscription.
            await self.cancel(msg_id)

    async def _wait_for_job_terminal(
        self,
        job_id: str,
        *,
        poll_interval_s: float,
        timeout_s: float | None,
        on_progress: Callable[[JobStatus], None] | None,
    ) -> ControlRunTerminal:
        deadline = time.monotonic() + timeout_s if timeout_s is not None else None
        while True:
            status = await self.job_status(job_id)
            if status.state in _TERMINAL_JOB_STATES:
                result = await self.job_result(job_id)
                return _terminal_from_result(status.state, result)
            if on_progress is not None:
                on_progress(status)

            if deadline is not None and time.monotonic() >= deadline:
                raise TimeoutError(f"Timed out waiting for terminal state for {job_id}")
            if poll_interval_s > 0:
                await asyncio.sleep(poll_interval_s)

    # -- internals --

    def _next_id(self) -> str:
        self._counter += 1
        return f"py-{self._counter}"

    def _command_payload(
        self,
        msg_id: str,
        name: str,
        args: dict[str, Any],
        timeout_ms: int | None,
        idempotency_key: str | None,
    ) -> dict[str, Any]:
        payload: dict[str, Any] = {
            "type": "command_call",
            "id": msg_id,
            "name": name,
            "args": args,
        }
        if self.lease_id is not None:
            payload["leaseId"] = self.lease_id
        if timeout_ms is not None:
            payload["timeoutMs"] = timeout_ms
        if idempotency_key is not None:
            payload["idempotencyKey"] = idempotency_key
        return payload

    def _job_payload(self, message_type: str, msg_id: str, job_id: str) -> dict[str, Any]:
        payload: dict[str, Any] = {"type": message_type, "id": msg_id, "jobId": job_id}
        if self.lease_id is not None:
            payload["leaseId"] = self.lease_id
        return payload

    async def _request(
        self, payload: dict[str, Any], msg_id: str, *, budget_s: float = CLIENT_TIMEOUT_S
    ) -> dict[str, Any]:
        """Send *payload* and wait for a response whose ``id`` matches *msg_id*."""
        ws = self._require_ws()

        await ws.send(json.dumps(payload))

        # Consume messages until we get our id (server may send broadcasts).
        deadline = time.monotonic() + budget_s
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError(f"Timed out waiting for response to {payload['type']}")

            raw = await asyncio.wait_for(ws.recv(), timeout=remaining)
            resp: dict[str, Any] = json.loads(raw)

            # Unsolicited server notification — log and keep waiting.
            if resp.get("type") == "assembly_reload":
                asm = resp.get("assembly") or "unknown"
                _log.warning("Assembly reloaded: %s. REPL session reset.", asm)
                continue

            if resp.get("id") == msg_id:
                if resp.get("type") in {"eval_error", "select_evaluator_error"}:
                    raise EvalError(
                        message=resp.get("message", resp.get("error", "unknown")),
                        kind=resp.get("errorKind", "unknown"),
                        stack_trace=resp.get("stackTrace"),
                    )
                if resp.get("type") == "command_error":
                    raise ControlCommandError(
                        _parse_control_error(resp.get("error")),
                        [_parse_control_error(item) for item in resp.get("diagnostics", [])],
                    )
                return resp


def _parse_control_error(data: object) -> ControlError:
    if not isinstance(data, dict):
        return ControlError(
            "internal",
            "missingError",
            "Control command failed without error details.",
            False,
        )
    payload = cast("dict[str, Any]", data)
    return ControlError(
        kind=str(payload.get("kind", "")),
        code=str(payload.get("code", "")),
        message=str(payload.get("message", "")),
        retryable=bool(payload.get("retryable", False)),
        details=payload.get("details"),
    )


def _resolve_token_source(token_source: str | TokenSource | None) -> str | None:
    if token_source is None:
        return None
    if isinstance(token_source, str):
        return token_source
    return token_source.resolve_token()


def _missing_commands(
    commands: Sequence[ControlCommandDescriptor], required: Sequence[str] | None
) -> list[str]:
    if not required:
        return []
    available = {command.name for command in commands}
    return [name for name in required if name not in available]


def _control_error(kind: str, code: str) -> ControlError:
    messages = {
        "authFailed": "Control authentication failed.",
        "leaseAcquisitionFailed": "Control lease acquisition failed.",
    }
    return ControlError(kind, code, messages.get(code, "Control operation failed."), False)


def _terminal_from_result(state: str, result: CommandResult) -> ControlRunTerminal:
    terminal_status = state
    if terminal_status == "completed" and result.status != "ok":
        terminal_status = "failed"
    if terminal_status == "completed":
        return ControlRunTerminal(
            status="completed",
            result=result.result,
            artifacts=result.artifacts,
            diagnostics=result.diagnostics,
        )
    return ControlRunTerminal(
        status=terminal_status,
        result=result.result,
        artifacts=result.artifacts,
        diagnostics=result.diagnostics,
        error=_terminal_error(terminal_status, result.diagnostics),
    )


def _terminal_error(status: str, diagnostics: list[ControlError]) -> ControlError:
    if diagnostics:
        return diagnostics[0]
    if status == "cancelled":
        return ControlError(
            "cancelled",
            "jobCancelled",
            "Job cancelled after Ctrl-C.",
            False,
        )
    return ControlError(
        "command_failed",
        "jobFailed",
        "Job failed.",
        False,
    )


def _unconfirmed_interrupt_terminal(mutates_state: bool) -> ControlRunTerminal:
    if mutates_state:
        return ControlRunTerminal(
            status="interrupted",
            error=ControlError(
                "interrupted",
                "interruptUnconfirmed",
                "Cancellation was requested for a mutating job, but no terminal job state "
                "was observed before the interrupt grace timeout. Do not retry until the "
                "previous job's terminal state is confirmed.",
                False,
            ),
        )
    return ControlRunTerminal(
        status="abandoned",
        error=ControlError(
            "cancelled",
            "readOnlyJobAbandoned",
            "Cancellation was requested for a non-mutating job, but no terminal job state "
            "was observed before the interrupt grace timeout. The read-only command can "
            "be retried.",
            True,
        ),
    )


def _parse_descriptor(data: dict[str, Any]) -> ControlCommandDescriptor:
    return ControlCommandDescriptor(
        name=data["name"],
        version=int(data["version"]),
        kind=data["kind"],
        mutates_state=bool(data.get("mutatesState", False)),
        args_schema=data.get("argsSchema", {}),
        result_schema=data.get("resultSchema", {}),
    )


def _parse_artifact(data: dict[str, Any]) -> ArtifactRef:
    return ArtifactRef(
        logical_name=data["logicalName"],
        uri=data["uri"],
        path=data.get("path"),
        content_type=data["contentType"],
        byte_size=int(data["byteSize"]),
        sha256=data["sha256"],
        finalized=bool(data["finalized"]),
    )


def _parse_command_result(data: dict[str, Any]) -> CommandResult:
    return CommandResult(
        status=data["status"],
        result=data.get("result", {}),
        artifacts=[_parse_artifact(item) for item in data.get("artifacts", [])],
        diagnostics=[_parse_control_error(item) for item in data.get("diagnostics", [])],
        state=data.get("state"),
    )
