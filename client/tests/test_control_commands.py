from __future__ import annotations

import pytest

from hotrepl import Client

from ._fake_control_server import fake_control_server

pytestmark = pytest.mark.no_hotrepl_server


@pytest.mark.asyncio
async def test_describe_commands_sends_command_describe_and_parses_descriptors() -> None:
    async with fake_control_server(
        {
            "command_describe": lambda message: {
                "type": "command_describe_result",
                "id": message["id"],
                "commands": [
                    {
                        "name": "archive.info",
                        "version": 1,
                        "kind": "sync",
                        "mutatesState": False,
                        "argsSchema": {"type": "object"},
                        "resultSchema": {"type": "object"},
                    }
                ],
            }
        }
    ) as (url, messages):
        client = Client(url)
        await client.connect()

        commands = await client.describe_commands()

        assert messages[0]["type"] == "command_describe"
        assert commands[0].name == "archive.info"
        assert commands[0].kind == "sync"
        await client.close()


@pytest.mark.asyncio
async def test_call_sends_command_call_and_parses_result() -> None:
    async with fake_control_server(
        {
            "command_call": lambda message: {
                "type": "command_result",
                "id": message["id"],
                "status": "ok",
                "result": {"echo": message["args"]["value"]},
                "artifacts": [],
                "diagnostics": [],
            }
        }
    ) as (url, messages):
        client = Client(url)
        await client.connect()

        result = await client.call("archive.echo", {"value": "ok"}, timeout_ms=1000)

        assert messages[0]["type"] == "command_call"
        assert messages[0]["name"] == "archive.echo"
        assert messages[0]["timeoutMs"] == 1000
        assert result.result == {"echo": "ok"}
        assert result.artifacts == []
        await client.close()
