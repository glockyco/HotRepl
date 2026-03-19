"""Shared fixtures for HotRepl smoke tests.

Session-scoped reachability probe skips ALL tests when no server is available.
Per-test `client` fixture provides a connected, reset Client.
"""

from __future__ import annotations

import asyncio
import os
from typing import TYPE_CHECKING

import pytest
import websockets

if TYPE_CHECKING:
    from collections.abc import AsyncGenerator

    from hotrepl import Client

HOTREPL_URL = os.environ.get("HOTREPL_URL", "ws://localhost:18590")
_PROBE_TIMEOUT_S = 2.0


def _probe_server(url: str) -> bool:
    """Attempt a WebSocket connection to the HotRepl server."""

    async def _try() -> bool:
        try:
            ws = await asyncio.wait_for(
                websockets.connect(url),
                timeout=_PROBE_TIMEOUT_S,
            )
            await ws.close()
        except (OSError, TimeoutError):
            return False
        else:
            return True

    loop = asyncio.new_event_loop()
    try:
        return loop.run_until_complete(_try())
    finally:
        loop.close()


_probe_result: dict[str, bool] = {}


def pytest_collection_modifyitems(config: pytest.Config, items: list[pytest.Item]) -> None:
    if "reachable" not in _probe_result:
        _probe_result["reachable"] = _probe_server(HOTREPL_URL)

    if _probe_result["reachable"]:
        return

    reason = f"No HotRepl server at {HOTREPL_URL}"
    skip_marker = pytest.mark.skip(reason=reason)
    for item in items:
        item.add_marker(skip_marker)


@pytest.fixture
async def client() -> AsyncGenerator[Client, None]:
    """Per-test fixture: connected Client with clean state."""
    from hotrepl import Client

    c = Client(url=HOTREPL_URL)
    await c.connect()
    await c.reset()
    try:
        yield c
    finally:
        await c.close()
