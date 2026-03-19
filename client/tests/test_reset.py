"""Tests for REPL state persistence and reset."""

from __future__ import annotations

import pytest

from hotrepl import Client, EvalError

pytestmark = pytest.mark.asyncio


async def test_variable_persists_across_evals(client: Client) -> None:
    await client.eval("var persistTest = 99;")
    resp = await client.eval("persistTest")
    assert resp["value"] == "99"


async def test_reset_clears_state(client: Client) -> None:
    await client.eval("var resetTarget = 1;")
    await client.reset()
    with pytest.raises(EvalError) as exc_info:
        await client.eval("resetTarget")
    assert exc_info.value.kind == "compilation"


async def test_reset_response_format(client: Client) -> None:
    resp = await client.reset()
    assert resp["type"] == "reset_result"
    assert resp["success"] is True
    assert "id" in resp
