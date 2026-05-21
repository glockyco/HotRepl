"""Structured CLI output helpers for human and agent automation."""

from __future__ import annotations

import json
import sys
from dataclasses import asdict, dataclass, is_dataclass
from typing import Any, TextIO, cast

SCHEMA_VERSION = 1


@dataclass(frozen=True)
class CliError:
    """Machine-readable CLI error."""

    kind: str
    code: str
    message: str
    retryable: bool
    remediation: str | None = None
    details: dict[str, Any] | None = None


def exit_code_for_error(error: CliError) -> int:
    """Map a structured error to the stable HotRepl CLI exit-code categories."""
    if error.code == "readOnlyJobAbandoned":
        return 10

    code_by_kind = {
        "server_unreachable": 3,
        "auth_failed": 4,
        "lease_conflict": 5,
        "lease_required": 5,
        "timeout": 6,
        "interrupted": 9,
        "cancelled": 8,
        "command_failed": 7,
        "invalid_request": 2,
        "validation_failed": 2,
    }
    if error.code in {"readinessTimeout", "readinessFailed"}:
        return 6
    return code_by_kind.get(error.kind, 1)


def emit_json_success(
    command: str,
    data: object,
    *,
    stdout: TextIO | None = None,
    duration_ms: int | None = None,
) -> int:
    """Emit a short-command success envelope and return exit code 0."""
    stream = stdout or sys.stdout
    payload: dict[str, object] = {
        "schemaVersion": SCHEMA_VERSION,
        "ok": True,
        "command": command,
        "data": _jsonable(data),
    }
    if duration_ms is not None:
        payload["meta"] = {"durationMs": duration_ms}
    _write_json_line(stream, payload, pretty=True)
    return 0


def emit_json_error(
    command: str,
    error: CliError,
    *,
    stdout: TextIO | None = None,
    stderr: TextIO | None = None,
) -> int:
    """Emit a short-command failure envelope and return the mapped exit code."""
    del stdout
    stream = stderr or sys.stderr
    payload = {
        "schemaVersion": SCHEMA_VERSION,
        "ok": False,
        "command": command,
        "error": _error_payload(error),
    }
    _write_json_line(stream, payload, pretty=True)
    return exit_code_for_error(error)


def emit_jsonl_event(
    command: str,
    phase: str,
    status: str,
    *,
    stdout: TextIO | None = None,
    **fields: object,
) -> None:
    """Emit one JSON Lines event for a long-running command."""
    stream = stdout or sys.stdout
    payload: dict[str, object] = {
        "schemaVersion": SCHEMA_VERSION,
        "command": command,
        "phase": phase,
        "status": status,
    }
    for key, value in fields.items():
        if value is not None:
            payload[key] = _jsonable(value)
    _write_json_line(stream, payload, pretty=False)


def _error_payload(error: CliError) -> dict[str, object]:
    payload: dict[str, object] = {
        "kind": error.kind,
        "code": error.code,
        "message": error.message,
        "retryable": error.retryable,
    }
    if error.remediation is not None:
        payload["remediation"] = error.remediation
    if error.details is not None:
        payload["details"] = error.details
    return payload


def _write_json_line(stream: TextIO, payload: object, *, pretty: bool) -> None:
    if pretty:
        stream.write(json.dumps(payload, indent=2, sort_keys=True))
    else:
        stream.write(json.dumps(payload, separators=(",", ":"), sort_keys=True))
    stream.write("\n")


def _jsonable(value: object) -> object:
    if is_dataclass(value) and not isinstance(value, type):
        return _jsonable(asdict(value))
    if isinstance(value, list):
        items = cast("list[object]", value)
        return [_jsonable(item) for item in items]
    if isinstance(value, tuple):
        items = cast("tuple[object, ...]", value)
        return [_jsonable(item) for item in items]
    if isinstance(value, dict):
        mapping = cast("dict[object, object]", value)
        return {str(key): _jsonable(item) for key, item in mapping.items()}
    return value
