"""Edge-case tests: empty code, unicode, rapid-fire, long code."""

from __future__ import annotations

import pytest

from hotrepl import Client, EvalError

pytestmark = pytest.mark.asyncio


async def test_empty_code(client: Client) -> None:
    """Empty string should not crash the server."""
    try:
        resp = await client.eval("")
        assert resp["type"] == "eval_result"
    except EvalError:
        pass  # Either outcome is acceptable; must not crash.


async def test_whitespace_only_code(client: Client) -> None:
    try:
        resp = await client.eval("   ")
        assert resp["type"] == "eval_result"
    except EvalError:
        pass


async def test_unicode_string(client: Client) -> None:
    resp = await client.eval('"héllo wörld 日本語"')
    assert resp["type"] == "eval_result"
    assert "héllo" in resp.get("value", "")


async def test_multiline_code(client: Client) -> None:
    code = "var a = 1;\nvar b = 2;\na + b"
    resp = await client.eval(code)
    assert resp["value"] == "3"


async def test_long_code(client: Client) -> None:
    lines = [f"var v{i} = {i};" for i in range(200)]
    lines.append("v199")
    code = "\n".join(lines)
    resp = await client.eval(code)
    assert resp["value"] == "199"


async def test_special_characters_in_string(client: Client) -> None:
    # String with quotes, backslashes, and newlines
    resp = await client.eval(r'"hello \"world\"\n\\path"')
    assert resp["type"] == "eval_result"


async def test_rapid_fire_evals(client: Client) -> None:
    for i in range(5):
        resp = await client.eval(str(i))
        assert resp["value"] == str(i)
