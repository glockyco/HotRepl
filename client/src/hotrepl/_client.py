"""Async WebSocket client for the HotRepl C# REPL protocol."""

from __future__ import annotations

import asyncio
import json
import time
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from collections.abc import AsyncGenerator
    from typing import Self

import websockets

DEFAULT_URL = "ws://localhost:18590"
CLIENT_TIMEOUT_S = 30.0  # Hard ceiling on any single round-trip


class EvalError(Exception):
    """Raised when the server returns an eval_error response."""

    def __init__(self, message: str, kind: str, stack_trace: str | None = None) -> None:
        self.kind = kind
        self.stack_trace = stack_trace
        super().__init__(message)


class ServerUnreachableError(Exception):
    """Cannot reach the HotRepl server."""


class Client:
    """Async WebSocket client for HotRepl eval requests."""

    def __init__(self, url: str = DEFAULT_URL) -> None:
        self.url = url
        self._ws: websockets.ClientConnection | None = None
        self._counter = 0
        self.handshake: dict[str, Any] | None = None

    # -- context manager --

    async def __aenter__(self) -> Self:
        await self.connect()
        return self

    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: Any,
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

        assert self._ws is not None, "call connect() first"
        raw = await asyncio.wait_for(self._ws.recv(), timeout=CLIENT_TIMEOUT_S)
        self.handshake = json.loads(raw)
        return self.handshake

    async def close(self) -> None:
        if self._ws is not None:
            await self._ws.close()
            self._ws = None

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
        return await self._request(payload, msg_id, timeout=client_timeout)

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
        assert self._ws is not None, "call connect() first"
        await self._ws.send(json.dumps(payload))

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
        assert self._ws is not None, "call connect() first"
        await self._ws.send(json.dumps(payload))

        try:
            while True:
                raw = await asyncio.wait_for(self._ws.recv(), timeout=CLIENT_TIMEOUT_S)
                resp: dict[str, Any] = json.loads(raw)

                if resp.get("id") != msg_id:
                    continue  # Not ours; skip.

                yield resp

                if resp.get("final", False):
                    return
        except GeneratorExit:
            # Generator was closed (e.g. Ctrl-C) — cancel the subscription.
            await self.cancel(msg_id)

    # -- internals --

    def _next_id(self) -> str:
        self._counter += 1
        return f"py-{self._counter}"

    async def _request(
        self, payload: dict[str, Any], msg_id: str, *, timeout: float = CLIENT_TIMEOUT_S
    ) -> dict[str, Any]:
        """Send *payload* and wait for a response whose ``id`` matches *msg_id*."""
        assert self._ws is not None, "call connect() first"

        await self._ws.send(json.dumps(payload))

        # Consume messages until we get our id (server may send broadcasts).
        deadline = time.monotonic() + timeout
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError(f"Timed out waiting for response to {payload['type']}")

            raw = await asyncio.wait_for(self._ws.recv(), timeout=remaining)
            resp: dict[str, Any] = json.loads(raw)

            if resp.get("id") == msg_id:
                if resp.get("type") == "eval_error":
                    raise EvalError(
                        message=resp.get("message", resp.get("error", "unknown")),
                        kind=resp.get("errorKind", "unknown"),
                        stack_trace=resp.get("stackTrace"),
                    )
                return resp
