"""Tests for eval error categorization (compilation vs runtime)."""

from __future__ import annotations

import pytest

from hotrepl import Client, EvalError

pytestmark = pytest.mark.asyncio


async def test_compilation_error(client: Client) -> None:
    with pytest.raises(EvalError) as exc_info:
        await client.eval("int x = ;")
    assert exc_info.value.kind == "compilation"


async def test_runtime_error(client: Client) -> None:
    with pytest.raises(EvalError) as exc_info:
        await client.eval('throw new System.Exception("boom");')
    assert exc_info.value.kind == "runtime"
    assert "boom" in str(exc_info.value)


async def test_runtime_error_has_stack_trace(client: Client) -> None:
    with pytest.raises(EvalError) as exc_info:
        await client.eval('throw new System.Exception("trace");')
    # stack_trace may or may not be present, but the field should exist
    assert hasattr(exc_info.value, "stack_trace")


async def test_undefined_variable_is_compilation_error(client: Client) -> None:
    with pytest.raises(EvalError) as exc_info:
        await client.eval("nonexistentVariable")
    assert exc_info.value.kind == "compilation"


async def test_division_by_zero(client: Client) -> None:
    with pytest.raises(EvalError) as exc_info:
        await client.eval("1 / (1 - 1)")
    assert exc_info.value.kind == "runtime"
