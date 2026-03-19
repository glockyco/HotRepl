"""Tests for the subscribe (watch) protocol."""

from __future__ import annotations

import pytest

from hotrepl import Client

pytestmark = pytest.mark.asyncio

_SUBSCRIBE_TIMEOUT_MS = 30000  # Generous: ticks depend on game frames.


async def test_subscribe_with_limit(client: Client) -> None:
    results: list[dict] = []
    async for msg in client.subscribe("1 + 1", limit=3, timeout_ms=_SUBSCRIBE_TIMEOUT_MS):
        results.append(msg)

    assert len(results) == 3
    seqs = [r["seq"] for r in results]
    assert seqs == [1, 2, 3]
    assert results[-1].get("final") is True


async def test_subscribe_values(client: Client) -> None:
    results: list[dict] = []
    async for msg in client.subscribe("42", limit=1, timeout_ms=_SUBSCRIBE_TIMEOUT_MS):
        results.append(msg)

    assert len(results) == 1
    r = results[0]
    assert r["type"] == "subscribe_result"
    assert r["hasValue"] is True
    assert r["value"] == "42"


async def test_subscribe_has_duration(client: Client) -> None:
    async for msg in client.subscribe("1", limit=1, timeout_ms=_SUBSCRIBE_TIMEOUT_MS):
        assert "durationMs" in msg
        assert msg["durationMs"] >= 0


async def test_subscribe_error_terminates(client: Client) -> None:
    """Invalid code should terminate after MaxConsecutiveErrors (3)."""
    results: list[dict] = []
    async for msg in client.subscribe(
        "invalidVar", limit=10, timeout_ms=_SUBSCRIBE_TIMEOUT_MS
    ):
        results.append(msg)

    assert len(results) <= 3
    last = results[-1]
    assert last.get("final") is True
    assert last["type"] == "subscribe_error"
