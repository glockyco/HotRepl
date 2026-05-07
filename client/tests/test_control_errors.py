from __future__ import annotations

import pytest

from hotrepl import Client, ControlCommandError

from ._fake_control_server import fake_control_server

pytestmark = pytest.mark.no_hotrepl_server


@pytest.mark.asyncio
async def test_call_raises_typed_exception_for_command_error() -> None:
    async with fake_control_server(
        {
            "command_call": lambda message: {
                "type": "command_error",
                "id": message["id"],
                "status": "failed",
                "error": {
                    "kind": "validation_failed",
                    "code": "badArgs",
                    "message": "bad args",
                    "retryable": False,
                    "details": {"field": "entity"},
                },
                "diagnostics": [],
            }
        }
    ) as (url, _messages):
        client = Client(url)
        await client.connect()

        with pytest.raises(ControlCommandError) as exc:
            await client.call("archive.export", {"entity": None})

        assert exc.value.error.kind == "validation_failed"
        assert exc.value.error.code == "badArgs"
        assert exc.value.error.details == {"field": "entity"}
        await client.close()
