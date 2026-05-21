from __future__ import annotations

import io
import json

import pytest
from hotrepl._output import (
    CliError,
    emit_json_error,
    emit_json_success,
    emit_jsonl_event,
    exit_code_for_error,
)

pytestmark = pytest.mark.no_hotrepl_server


def test_json_success_envelope_contains_schema_command_data_and_meta() -> None:
    stdout = io.StringIO()

    code = emit_json_success("hotrepl.status", {"ready": True}, stdout=stdout, duration_ms=25)

    assert code == 0
    payload = json.loads(stdout.getvalue())
    assert payload == {
        "schemaVersion": 1,
        "ok": True,
        "command": "hotrepl.status",
        "data": {"ready": True},
        "meta": {"durationMs": 25},
    }


def test_json_error_envelope_goes_to_stderr_and_maps_exit_code() -> None:
    stdout = io.StringIO()
    stderr = io.StringIO()
    error = CliError(
        kind="auth_failed",
        code="invalidToken",
        message="Control-plane authentication failed.",
        retryable=False,
    )

    code = emit_json_error("hotrepl.wait", error, stdout=stdout, stderr=stderr)

    assert code == 4
    assert stdout.getvalue() == ""
    payload = json.loads(stderr.getvalue())
    assert payload["ok"] is False
    assert payload["command"] == "hotrepl.wait"
    assert payload["error"]["code"] == "invalidToken"


def test_jsonl_renderer_emits_exactly_one_complete_event() -> None:
    stdout = io.StringIO()

    emit_jsonl_event(
        "hotrepl.control.run", "job", "running", stdout=stdout, progress={"current": 1}
    )
    emit_jsonl_event(
        "hotrepl.control.run",
        "complete",
        "completed",
        stdout=stdout,
        result={},
        diagnostics=[],
        artifacts=[],
    )

    lines = [json.loads(line) for line in stdout.getvalue().splitlines()]
    assert [line["phase"] for line in lines] == ["job", "complete"]
    assert len([line for line in lines if line["phase"] == "complete"]) == 1
    assert lines[-1]["status"] == "completed"


def test_exit_code_mapping_covers_agent_error_categories() -> None:
    assert exit_code_for_error(CliError("server_unreachable", "serverUnreachable", "", True)) == 3
    assert exit_code_for_error(CliError("auth_failed", "invalidToken", "", False)) == 4
    assert exit_code_for_error(CliError("lease_conflict", "leaseAlreadyHeld", "", True)) == 5
    assert exit_code_for_error(CliError("timeout", "readinessTimeout", "", True)) == 6
    assert exit_code_for_error(CliError("command_failed", "jobFailed", "", False)) == 7
    assert exit_code_for_error(CliError("cancelled", "jobCancelled", "", False)) == 8
    assert exit_code_for_error(CliError("interrupted", "interruptUnconfirmed", "", False)) == 9
    assert exit_code_for_error(CliError("cancelled", "readOnlyJobAbandoned", "", True)) == 10
