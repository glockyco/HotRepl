from __future__ import annotations

import pytest
from hotrepl import Client

from ._fake_control_server import fake_control_server

pytestmark = pytest.mark.no_hotrepl_server


@pytest.mark.asyncio
async def test_start_job_returns_command_accepted() -> None:
    async with fake_control_server(
        {
            "command_call": lambda message: {
                "type": "command_accepted",
                "id": message["id"],
                "jobId": "job-1",
                "state": "accepted",
            }
        }
    ) as (url, messages):
        client = Client(url)
        await client.connect()

        accepted = await client.start_job("archive.export", {"entity": "item"})

        assert messages[0]["type"] == "command_call"
        assert messages[0]["name"] == "archive.export"
        assert accepted.job_id == "job-1"
        assert accepted.state == "accepted"
        await client.close()


@pytest.mark.asyncio
async def test_job_status_result_and_cancel_parse_responses() -> None:
    async with fake_control_server(
        {
            "job_status": lambda message: {
                "type": "job_status_result",
                "id": message["id"],
                "jobId": message["jobId"],
                "state": "running",
                "progress": {"done": 2, "total": 10},
            },
            "job_result": lambda message: {
                "type": "job_result",
                "id": message["id"],
                "jobId": message["jobId"],
                "state": "completed",
                "status": "ok",
                "result": {"count": 42},
                "artifacts": [
                    {
                        "logicalName": "items",
                        "uri": "file:///fixtures/items.json",
                        "path": "/fixtures/items.json",
                        "contentType": "application/json",
                        "byteSize": 100,
                        "sha256": "abc",
                        "finalized": True,
                    }
                ],
                "diagnostics": [],
            },
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

        status = await client.job_status("job-1")
        result = await client.job_result("job-1")
        cancel = await client.cancel_job("job-1")

        assert status.state == "running"
        assert status.progress == {"done": 2, "total": 10}
        assert result.result == {"count": 42}
        assert result.artifacts[0].logical_name == "items"
        assert cancel.accepted is True
        assert cancel.state == "cancelling"
        await client.close()
