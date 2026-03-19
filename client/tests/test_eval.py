"""Tests for basic eval expressions and statements."""

from __future__ import annotations

import pytest

from hotrepl import Client

pytestmark = pytest.mark.asyncio


async def test_eval_expression(client: Client) -> None:
    resp = await client.eval("1 + 1")
    assert resp["type"] == "eval_result"
    assert resp["hasValue"] is True
    assert resp["value"] == "2"


async def test_eval_string_expression(client: Client) -> None:
    resp = await client.eval('"hello" + " world"')
    assert resp["value"] == "hello world"


async def test_eval_statement_no_value(client: Client) -> None:
    resp = await client.eval("var x = 42;")
    assert resp["hasValue"] is False


async def test_eval_stdout_capture(client: Client) -> None:
    resp = await client.eval('Console.WriteLine("captured");')
    assert "captured" in resp.get("stdout", "")


async def test_eval_has_duration(client: Client) -> None:
    resp = await client.eval("1")
    duration = resp.get("durationMs")
    assert duration is not None
    assert duration >= 0


async def test_eval_value_type(client: Client) -> None:
    resp = await client.eval("42")
    assert "Int" in resp.get("valueType", "")


async def test_eval_bool(client: Client) -> None:
    resp = await client.eval("true")
    assert resp["value"] == "True"


async def test_eval_null_expression(client: Client) -> None:
    resp = await client.eval("(string)null")
    assert resp["type"] == "eval_result"
