from __future__ import annotations

import asyncio
import json
from typing import Any

import pytest
from hotrepl import Client, cli
from hotrepl.cli import build_parser

from ._fake_control_server import fake_control_server

pytestmark = pytest.mark.no_hotrepl_server


async def _invoke_handler(name: str, args: object) -> None:
    await vars(cli)[name](args)


def _descriptor(name: str, *, mutates: bool) -> dict[str, Any]:
    return {
        "name": name,
        "version": 1,
        "kind": "job",
        "mutatesState": mutates,
        "argsSchema": {"type": "object"},
        "resultSchema": {"type": "object"},
    }


def _job_result(message: dict[str, Any], *, status: str = "ok") -> dict[str, Any]:
    return {
        "type": "job_result",
        "id": message["id"],
        "jobId": message["jobId"],
        "state": "completed" if status == "ok" else status,
        "status": status,
        "result": {"count": 1} if status == "ok" else {},
        "artifacts": [],
        "diagnostics": [],
    }


@pytest.mark.asyncio
async def test_control_run_wait_jsonl_uses_one_connection_and_emits_one_terminal_event(
    capsys: pytest.CaptureFixture[str],
) -> None:
    status_calls = 0

    def job_status(message: dict[str, Any]) -> dict[str, Any]:
        nonlocal status_calls
        status_calls += 1
        state = "running" if status_calls == 1 else "completed"
        return {
            "type": "job_status_result",
            "id": message["id"],
            "jobId": message["jobId"],
            "state": state,
            "progress": {"current": status_calls, "total": 2},
        }

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
            "command_call": lambda message: {
                "type": "command_accepted",
                "id": message["id"],
                "jobId": "job-1",
                "state": "accepted",
            },
            "job_status": job_status,
            "job_result": _job_result,
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
                "control",
                "run",
                "export.batch",
                '{"entity":"item"}',
                "--lease",
                "--wait",
                "--jsonl",
                "--poll-interval",
                "0",
            ]
        )

        with pytest.raises(SystemExit) as exc:
            await _invoke_handler("_cmd_control", args)

        assert exc.value.code == 0
        events = [json.loads(line) for line in capsys.readouterr().out.splitlines()]
        assert [event["phase"] for event in events] == ["connect", "job", "complete"]
        assert events[-1]["status"] == "completed"
        assert events[-1]["result"] == {"count": 1}
        assert sum(event["phase"] == "complete" for event in events) == 1
        assert [message["type"] for message in messages] == [
            "control_auth",
            "command_describe",
            "lease_acquire",
            "command_call",
            "job_status",
            "job_status",
            "job_result",
        ]
        assert messages[3]["leaseId"] == "lease-1"


@pytest.mark.asyncio
async def test_run_control_job_cancellation_confirms_terminal_cancelled_state() -> None:
    first_status_seen = asyncio.Event()
    cancelled = False

    def job_status(message: dict[str, Any]) -> dict[str, Any]:
        if not first_status_seen.is_set():
            first_status_seen.set()
            state = "running"
        else:
            state = "cancelled" if cancelled else "running"
        return {
            "type": "job_status_result",
            "id": message["id"],
            "jobId": message["jobId"],
            "state": state,
        }

    def job_cancel(message: dict[str, Any]) -> dict[str, Any]:
        nonlocal cancelled
        cancelled = True
        return {
            "type": "job_cancel_result",
            "id": message["id"],
            "accepted": True,
            "state": "cancelling",
        }

    async with fake_control_server(
        {
            "command_call": lambda message: {
                "type": "command_accepted",
                "id": message["id"],
                "jobId": "job-1",
                "state": "accepted",
            },
            "job_status": job_status,
            "job_cancel": job_cancel,
            "job_result": lambda message: _job_result(message, status="cancelled"),
        }
    ) as (url, messages):
        client = Client(url)
        await client.connect()
        task = asyncio.create_task(
            client.run_control_job(
                "read.job",
                {},
                wait=True,
                poll_interval_s=60,
                interrupt_grace_s=0.2,
                mutates_state=False,
            )
        )
        await first_status_seen.wait()

        task.cancel()
        terminal = await task

        assert terminal.status == "cancelled"
        assert [message["type"] for message in messages] == [
            "command_call",
            "job_status",
            "job_cancel",
            "job_status",
            "job_result",
        ]
        await client.close()


@pytest.mark.asyncio
async def test_run_control_job_cancellation_distinguishes_mutating_from_read_only() -> None:
    first_status_seen = asyncio.Event()

    def job_status(message: dict[str, Any]) -> dict[str, Any]:
        first_status_seen.set()
        return {
            "type": "job_status_result",
            "id": message["id"],
            "jobId": message["jobId"],
            "state": "running",
        }

    async with fake_control_server(
        {
            "command_call": lambda message: {
                "type": "command_accepted",
                "id": message["id"],
                "jobId": "job-1",
                "state": "accepted",
            },
            "job_status": job_status,
            "job_cancel": lambda message: {
                "type": "job_cancel_result",
                "id": message["id"],
                "accepted": True,
                "state": "cancelling",
            },
        }
    ) as (url, _messages):
        client = Client(url)
        await client.connect()
        mutating = asyncio.create_task(
            client.run_control_job(
                "mutating.job",
                {},
                wait=True,
                poll_interval_s=60,
                interrupt_grace_s=0.01,
                mutates_state=True,
            )
        )
        await first_status_seen.wait()
        mutating.cancel()
        mutating_terminal = await mutating

        assert mutating_terminal.status == "interrupted"
        assert mutating_terminal.error is not None
        assert mutating_terminal.error.code == "interruptUnconfirmed"
        assert mutating_terminal.error.retryable is False
        await client.close()

    first_status_seen = asyncio.Event()
    async with fake_control_server(
        {
            "command_call": lambda message: {
                "type": "command_accepted",
                "id": message["id"],
                "jobId": "job-1",
                "state": "accepted",
            },
            "job_status": job_status,
            "job_cancel": lambda message: {
                "type": "job_cancel_result",
                "id": message["id"],
                "accepted": True,
                "state": "cancelling",
            },
        }
    ) as (url, _messages):
        client = Client(url)
        await client.connect()
        read_only = asyncio.create_task(
            client.run_control_job(
                "read.job",
                {},
                wait=True,
                poll_interval_s=60,
                interrupt_grace_s=0.01,
                mutates_state=False,
            )
        )
        await first_status_seen.wait()
        read_only.cancel()
        read_only_terminal = await read_only

        assert read_only_terminal.status == "abandoned"
        assert read_only_terminal.error is not None
        assert read_only_terminal.error.code == "readOnlyJobAbandoned"
        assert read_only_terminal.error.retryable is True
        await client.close()
