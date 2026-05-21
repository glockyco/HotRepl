from __future__ import annotations

import json
from typing import TYPE_CHECKING

import pytest
from hotrepl._discovery import discover_instances, select_instance
from hotrepl.cli import build_parser

if TYPE_CHECKING:
    from pathlib import Path

pytestmark = pytest.mark.no_hotrepl_server


def _write_instance(
    root: Path, name: str, *, host_name: str = "BepInEx", port: int = 18590
) -> None:
    root.mkdir(parents=True, exist_ok=True)
    (root / f"{name}.json").write_text(
        json.dumps(
            {
                "schemaVersion": 1,
                "instanceId": name,
                "url": f"ws://127.0.0.1:{port}",
                "bindHost": "127.0.0.1",
                "port": port,
                "host": {"name": host_name, "runtime": "Mono", "platform": "Unity"},
                "controlPlane": {"supported": True, "protocolVersion": 1},
                "auth": {"required": True, "fingerprint": "sha256:12345678"},
            }
        ),
        encoding="utf-8",
    )


def test_discover_instances_reads_candidate_documents_without_socket(tmp_path: Path) -> None:
    _write_instance(tmp_path, "one", host_name="BepInEx")
    _write_instance(tmp_path, "two", host_name="MelonLoader")

    result = discover_instances([tmp_path], host="BepInEx")

    assert [instance.instance_id for instance in result.instances] == ["one"]
    assert result.instances[0].url == "ws://127.0.0.1:18590"
    assert result.diagnostics == []


def test_discover_instances_reports_invalid_json_as_diagnostic(tmp_path: Path) -> None:
    tmp_path.mkdir(parents=True, exist_ok=True)
    (tmp_path / "broken.json").write_text("not json", encoding="utf-8")

    result = discover_instances([tmp_path])

    assert result.instances == []
    assert result.diagnostics[0].code == "invalidInstanceDocument"
    assert result.diagnostics[0].retryable is False


def test_select_instance_reports_ambiguity() -> None:
    result = discover_instances([])
    error = select_instance(result)

    assert error.error is not None
    assert error.error.code == "noInstancesFound"


def test_cli_discover_subcommand_parse() -> None:
    parser = build_parser()

    args = parser.parse_args(["discover", "--host", "BepInEx", "--profile", "ardenfall", "--json"])

    assert args.command == "discover"
    assert args.host == "BepInEx"
    assert args.profile == "ardenfall"
    assert args.json is True
