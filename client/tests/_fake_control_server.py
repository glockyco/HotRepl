from __future__ import annotations

import inspect
import json
from collections.abc import AsyncGenerator, Awaitable, Callable
from contextlib import asynccontextmanager
from typing import Any

import websockets

ResponseHandler = Callable[[dict[str, Any]], dict[str, Any] | Awaitable[dict[str, Any]]]


@asynccontextmanager
async def fake_control_server(
    handlers: dict[str, ResponseHandler],
) -> AsyncGenerator[tuple[str, list[dict[str, Any]]], None]:
    messages: list[dict[str, Any]] = []

    async def handle(websocket: websockets.ServerConnection) -> None:
        await websocket.send(
            json.dumps(
                {
                    "type": "handshake",
                    "version": "1.0",
                    "controlPlane": {"supported": True, "protocolVersion": 1},
                }
            )
        )
        async for raw in websocket:
            message: dict[str, Any] = json.loads(raw)
            messages.append(message)
            handler = handlers[message["type"]]
            response = handler(message)
            if inspect.isawaitable(response):
                response = await response
            await websocket.send(json.dumps(response))

    server = await websockets.serve(handle, "127.0.0.1", 0)
    try:
        assert server.sockets is not None
        port = server.sockets[0].getsockname()[1]
        yield f"ws://127.0.0.1:{port}", messages
    finally:
        server.close()
        await server.wait_closed()
