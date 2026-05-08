"""Tests for ping/pong round-trip."""

from __future__ import annotations

from typing import TYPE_CHECKING

import pytest

if TYPE_CHECKING:
    from hotrepl import Client

pytestmark = pytest.mark.asyncio


async def test_ping_returns_positive_ms(client: Client) -> None:
    ms = await client.ping()
    assert isinstance(ms, float)
    assert 0 < ms < 5000


async def test_ping_multiple(client: Client) -> None:
    for _ in range(3):
        ms = await client.ping()
        assert ms > 0
