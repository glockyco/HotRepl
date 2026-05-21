from __future__ import annotations

import json
from typing import TYPE_CHECKING, Any

import pytest
from hotrepl import Client, cli
from hotrepl._client import ControlCommandError
from hotrepl.cli import build_parser

from ._fake_control_server import fake_control_server

if TYPE_CHECKING:
    from pathlib import Path

pytestmark = pytest.mark.no_hotrepl_server


async def _invoke_handler(name: str, args: object) -> None:
    await vars(cli)[name](args)


def _descriptor(name: str, *, mutates: bool = False) -> dict[str, Any]:
    return {
        "name": name,
        "version": 1,
        "kind": "job" if mutates else "sync",
        "mutatesState": mutates,
        "argsSchema": {"type": "object"},
        "resultSchema": {"type": "object"},
    }


@pytest.mark.asyncio
async def test_prepare_control_authenticates_once_acquires_lease_and_requires_commands() -> None:
    async with fake_control_server(
        {
            "control_auth": lambda message: {
                "type": "control_auth_result",
                "id": message["id"],
                "ok": True,
                "sessionId": "session-1",
            },
            "command_describe": lambda message: {
                "type": "command_describe_result",
                "id": message["id"],
                "commands": [_descriptor("export.batch", mutates=True)],
            },
            "lease_acquire": lambda message: {
                "type": "lease_acquire_result",
                "id": message["id"],
                "ok": True,
                "leaseId": "lease-1",
            },
        },
        handshake={
            "type": "handshake",
            "version": "1.0",
            "controlPlane": {"supported": True, "protocolVersion": 1, "authRequired": True},
        },
    ) as (url, messages):
        client = Client(url)
        await client.connect()

        prepared = await client.prepare_control(
            token_source="secret-token",
            client_name="hotrepl-cli",
            acquire_lease=True,
            require_commands=["export.batch"],
        )

        assert prepared.auth is not None
        assert prepared.auth.session_id == "session-1"
        assert prepared.lease is not None
        assert prepared.lease.lease_id == "lease-1"
        assert [message["type"] for message in messages] == [
            "control_auth",
            "command_describe",
            "lease_acquire",
        ]
        assert messages[0]["token"] == "secret-token"
        assert messages[2]["sessionId"] == "session-1"
        await client.close()


@pytest.mark.asyncio
async def test_prepare_control_errors_when_required_commands_are_missing() -> None:
    async with fake_control_server(
        {
            "command_describe": lambda message: {
                "type": "command_describe_result",
                "id": message["id"],
                "commands": [_descriptor("export.batch")],
            }
        }
    ) as (url, _messages):
        client = Client(url)
        await client.connect()

        with pytest.raises(ControlCommandError) as exc:
            await client.prepare_control(require_commands=["missing.command"])

        assert exc.value.error.code == "missingCommands"
        assert exc.value.error.retryable is True
        await client.close()


@pytest.mark.asyncio
async def test_status_json_reports_active_connection_impact_and_profile_auth(
    tmp_path: Path, capsys: pytest.CaptureFixture[str], monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setenv("HOTREPL_TOKEN", "profile-secret")
    async with fake_control_server(
        {
            "control_auth": lambda message: {
                "type": "control_auth_result",
                "id": message["id"],
                "ok": True,
                "sessionId": "session-1",
            },
            "command_describe": lambda message: {
                "type": "command_describe_result",
                "id": message["id"],
                "commands": [_descriptor("export.batch")],
            },
        },
        handshake={
            "type": "handshake",
            "version": "1.0",
            "controlPlane": {"supported": True, "protocolVersion": 1, "authRequired": True},
        },
    ) as (url, messages):
        profile_file = tmp_path / "profiles.json"
        profile_file.write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "profiles": {
                        "demo": {
                            "url": url,
                            "auth": {"source": "env", "name": "HOTREPL_TOKEN"},
                        }
                    },
                }
            ),
            encoding="utf-8",
        )
        args = build_parser().parse_args(
            ["status", "--profile", "demo", "--profile-file", str(profile_file), "--json"]
        )

        with pytest.raises(SystemExit) as exc:
            await _invoke_handler("_cmd_status", args)

        assert exc.value.code == 0
        payload = json.loads(capsys.readouterr().out)
        assert payload["command"] == "hotrepl.status"
        data = payload["data"]
        assert data["connectionImpact"] == {
            "mode": "active-websocket",
            "mayReplaceActiveClient": True,
            "acquiresLease": False,
        }
        assert data["ready"] is True
        assert {check["name"]: check["status"] for check in data["checks"]}[
            "control.auth"
        ] == "pass"
        assert messages[0]["token"] == "profile-secret"


@pytest.mark.asyncio
async def test_status_json_reports_blocked_checks_after_socket_failure(
    capsys: pytest.CaptureFixture[str],
) -> None:
    args = build_parser().parse_args(["--url", "ws://127.0.0.1:9", "status", "--json"])

    with pytest.raises(SystemExit) as exc:
        await _invoke_handler("_cmd_status", args)

    assert exc.value.code == 0
    data = json.loads(capsys.readouterr().out)["data"]
    assert data["ready"] is False
    by_name = {check["name"]: check for check in data["checks"]}
    assert by_name["socket.connect"]["status"] == "fail"
    assert by_name["control.describe"]["status"] == "blocked"
    assert by_name["commands.required"]["status"] == "unobserved"


@pytest.mark.asyncio
async def test_wait_retries_until_commands_visible_before_acquiring_lease(
    capsys: pytest.CaptureFixture[str],
) -> None:
    describe_calls = 0

    def describe(message: dict[str, Any]) -> dict[str, Any]:
        nonlocal describe_calls
        describe_calls += 1
        commands = [] if describe_calls == 1 else [_descriptor("run.begin", mutates=True)]
        return {"type": "command_describe_result", "id": message["id"], "commands": commands}

    async with fake_control_server(
        {
            "control_auth": lambda message: {
                "type": "control_auth_result",
                "id": message["id"],
                "ok": True,
                "sessionId": "session-1",
            },
            "command_describe": describe,
            "lease_acquire": lambda message: {
                "type": "lease_acquire_result",
                "id": message["id"],
                "ok": True,
                "leaseId": "lease-1",
            },
        },
        handshake={
            "type": "handshake",
            "version": "1.0",
            "controlPlane": {"supported": True, "protocolVersion": 1, "authRequired": True},
        },
    ) as (url, messages):
        args = build_parser().parse_args(
            [
                "--url",
                url,
                "wait",
                "--lease",
                "--commands",
                "run.begin",
                "--timeout",
                "1",
                "--interval",
                "0",
                "--json",
            ]
        )

        with pytest.raises(SystemExit) as exc:
            await _invoke_handler("_cmd_wait", args)

        assert exc.value.code == 0
        data = json.loads(capsys.readouterr().out)["data"]
        assert data["ready"] is True
        assert [message["type"] for message in messages] == [
            "control_auth",
            "command_describe",
            "command_describe",
            "lease_acquire",
        ]
        assert messages[-1]["sessionId"] == "session-1"
