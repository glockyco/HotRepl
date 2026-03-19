"""Tests for autocomplete/completion protocol."""

from __future__ import annotations

import pytest

from hotrepl import Client

pytestmark = pytest.mark.asyncio


async def test_complete_returns_list(client: Client) -> None:
    results = await client.complete("Console.")
    assert isinstance(results, list)
    assert len(results) > 0


async def test_complete_filters_by_prefix(client: Client) -> None:
    results = await client.complete("Console.Write")
    assert len(results) > 0
    for item in results:
        assert "Write" in item


async def test_complete_empty_code(client: Client) -> None:
    results = await client.complete("")
    assert isinstance(results, list)


async def test_complete_with_cursor_pos(client: Client) -> None:
    # Cursor at position 8 is right after "Console." in "Console. + 42"
    results = await client.complete("Console. + 42", cursor_pos=8)
    assert isinstance(results, list)
    assert len(results) > 0
