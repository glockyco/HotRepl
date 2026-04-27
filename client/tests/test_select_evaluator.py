from __future__ import annotations

import pytest

from hotrepl import Client, EvalError

pytestmark = pytest.mark.asyncio


async def test_select_current_evaluator(client: Client) -> None:
    assert client.handshake is not None
    evaluator = client.handshake.get("evaluator") or {}
    current = evaluator.get("name")
    if not isinstance(current, str) or not current:
        pytest.skip("server does not advertise evaluator metadata")

    result = await client.select_evaluator(current)

    assert result["type"] == "select_evaluator_result"
    assert result["success"] is True
    assert result["evaluator"] == current


async def test_select_unknown_evaluator_reports_unsupported(client: Client) -> None:
    assert client.handshake is not None
    available = client.handshake.get("availableEvaluators")
    if not isinstance(available, list) or not available:
        pytest.skip("server does not advertise evaluator selection")
    with pytest.raises(EvalError) as exc_info:
        await client.select_evaluator("Missing.Evaluator")

    assert exc_info.value.kind == "unsupported"
