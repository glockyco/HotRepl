"""Passive HotRepl instance document discovery."""

from __future__ import annotations

import json
import os
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, cast

from hotrepl._output import CliError


@dataclass(frozen=True)
class InstanceDocument:
    """One candidate HotRepl instance discovered from disk."""

    instance_id: str
    url: str
    bind_host: str
    port: int
    host: dict[str, Any]
    control_plane: dict[str, Any]
    auth: dict[str, Any]
    path: Path

    @classmethod
    def from_json(cls, path: Path, data: object) -> InstanceDocument:
        if not isinstance(data, dict):
            raise TypeError("Instance document must contain a JSON object.")
        mapping = cast("dict[str, object]", data)
        return cls(
            instance_id=str(mapping.get("instanceId", path.stem)),
            url=str(mapping.get("url", "")),
            bind_host=str(mapping.get("bindHost", "")),
            port=_int_value(mapping.get("port")),
            host=_object(mapping.get("host")),
            control_plane=_object(mapping.get("controlPlane")),
            auth=_object(mapping.get("auth")),
            path=path,
        )

    def to_json(self) -> dict[str, object]:
        return {
            "instanceId": self.instance_id,
            "url": self.url,
            "bindHost": self.bind_host,
            "port": self.port,
            "host": self.host,
            "controlPlane": self.control_plane,
            "auth": self.auth,
            "path": str(self.path),
        }


@dataclass(frozen=True)
class DiscoveryResult:
    """Result of passive instance discovery."""

    instances: list[InstanceDocument]
    diagnostics: list[CliError]

    def to_json(self) -> dict[str, object]:
        return {
            "instances": [instance.to_json() for instance in self.instances],
            "diagnostics": [diagnostic.__dict__ for diagnostic in self.diagnostics],
        }


@dataclass(frozen=True)
class InstanceSelection:
    """A single selected instance or a structured selection error."""

    instance: InstanceDocument | None
    error: CliError | None


def default_instance_roots() -> list[Path]:
    """Return platform-specific candidate instance document directories."""
    roots: list[Path] = []
    xdg_runtime = os.environ.get("XDG_RUNTIME_DIR")
    if xdg_runtime:
        roots.append(Path(xdg_runtime) / "hotrepl" / "instances")
    if sys.platform == "darwin":
        roots.append(Path.home() / "Library" / "Application Support" / "HotRepl" / "instances")
    elif os.name == "nt":
        local_app_data = os.environ.get("LOCALAPPDATA")
        if local_app_data:
            roots.append(Path(local_app_data) / "HotRepl" / "instances")
    else:
        roots.append(Path.home() / ".local" / "state" / "hotrepl" / "instances")
    return roots


def discover_instances(
    roots: list[str | Path] | None = None,
    *,
    host: str | None = None,
    instance_filter: dict[str, Any] | None = None,
) -> DiscoveryResult:
    """Read candidate instance documents without opening a WebSocket."""
    selected_roots = (
        [Path(root).expanduser() for root in roots]
        if roots is not None
        else default_instance_roots()
    )
    instances: list[InstanceDocument] = []
    diagnostics: list[CliError] = []
    for root in selected_roots:
        if not root.exists():
            continue
        for path in sorted(root.glob("*.json")):
            try:
                data = json.loads(path.read_text(encoding="utf-8"))
                instance = InstanceDocument.from_json(path, data)
            except (OSError, TypeError, ValueError, json.JSONDecodeError) as exc:
                diagnostics.append(
                    CliError(
                        kind="invalid_request",
                        code="invalidInstanceDocument",
                        message=f"Invalid HotRepl instance document '{path}': {exc}",
                        retryable=False,
                    )
                )
                continue
            if host is not None and instance.host.get("name") != host:
                continue
            if not _matches_filter(instance, instance_filter):
                continue
            instances.append(instance)
    return DiscoveryResult(instances, diagnostics)


def select_instance(result: DiscoveryResult) -> InstanceSelection:
    """Select exactly one instance or return a structured ambiguity error."""
    if len(result.instances) == 1:
        return InstanceSelection(result.instances[0], None)
    if not result.instances:
        return InstanceSelection(
            None,
            CliError(
                kind="server_unreachable",
                code="noInstancesFound",
                message="No HotRepl instance documents were found.",
                retryable=True,
            ),
        )
    return InstanceSelection(
        None,
        CliError(
            kind="invalid_request",
            code="multipleInstancesFound",
            message=(
                "Multiple HotRepl instances matched; choose one with --profile, --host, or --url."
            ),
            retryable=False,
        ),
    )


def _object(value: object) -> dict[str, Any]:
    if isinstance(value, dict):
        return cast("dict[str, Any]", value)
    return {}


def _matches_filter(instance: InstanceDocument, instance_filter: dict[str, Any] | None) -> bool:
    if not instance_filter:
        return True
    instance_id = instance_filter.get("instanceId")
    if instance_id is not None and str(instance_id) != instance.instance_id:
        return False
    host = instance_filter.get("host") or instance_filter.get("hostName")
    if host is not None and str(host) != str(instance.host.get("name", "")):
        return False
    port = instance_filter.get("port")
    if port is not None and _int_value(port) != instance.port:
        return False
    url = instance_filter.get("url")
    return not (url is not None and str(url) != instance.url)


def _int_value(value: object) -> int:
    if isinstance(value, int):
        return value
    if isinstance(value, float | str):
        return int(value)
    return 0
