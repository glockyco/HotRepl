"""Local HotRepl profile loading and auth-source resolution."""

from __future__ import annotations

import configparser
import json
import os
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Self, cast


@dataclass(frozen=True)
class AuthSource:
    """A local handle that can resolve a control auth token."""

    source: str
    name: str | None = None
    path: str | None = None
    section: str | None = None
    key: str | None = None

    @classmethod
    def from_json(cls, data: object) -> Self | None:
        if not isinstance(data, dict):
            return None
        mapping = cast("dict[str, object]", data)
        return cls(
            source=str(mapping.get("source", "")),
            name=_optional_str(mapping.get("name")),
            path=_optional_str(mapping.get("path")),
            section=_optional_str(mapping.get("section")),
            key=_optional_str(mapping.get("key")),
        )

    def resolve_token(self) -> str | None:
        resolvers = {
            "env": self._resolve_env,
            "token-file": self._resolve_token_file,
            "bepinex-config": self._resolve_bepinex_config,
            "token": self._resolve_inline_token,
        }
        resolver = resolvers.get(self.source)
        if resolver is None:
            return None
        return resolver()

    def _resolve_env(self) -> str | None:
        if not self.name:
            return None
        return os.environ.get(self.name)

    def _resolve_token_file(self) -> str | None:
        if not self.path:
            return None
        return Path(self.path).expanduser().read_text(encoding="utf-8").strip()

    def _resolve_bepinex_config(self) -> str | None:
        if not self.path or not self.section or not self.key:
            return None
        parser = configparser.ConfigParser()
        parser.read(Path(self.path).expanduser(), encoding="utf-8")
        return parser.get(self.section, self.key, fallback=None)

    def _resolve_inline_token(self) -> str | None:
        return self.key

    def to_safe_json(self) -> dict[str, object]:
        payload: dict[str, object] = {"source": self.source}
        if self.name is not None:
            payload["name"] = self.name
        if self.path is not None:
            payload["path"] = self.path
        if self.section is not None:
            payload["section"] = self.section
        if self.key is not None and self.source != "token":
            payload["key"] = self.key
        return payload


@dataclass(frozen=True)
class Profile:
    """A client-side profile that resolves endpoint and auth handles."""

    name: str
    url: str | None
    instance: dict[str, Any]
    auth: AuthSource | None

    @classmethod
    def from_json(cls, name: str, data: object) -> Self:
        if not isinstance(data, dict):
            raise TypeError(f"Profile '{name}' must be a JSON object.")
        mapping = cast("dict[str, object]", data)
        instance = mapping.get("instance") if isinstance(mapping.get("instance"), dict) else {}
        return cls(
            name=name,
            url=_optional_str(mapping.get("url")),
            instance=cast("dict[str, Any]", instance),
            auth=AuthSource.from_json(mapping.get("auth")),
        )

    def resolve_token(self) -> str | None:
        if self.auth is None:
            return None
        return self.auth.resolve_token()

    def to_safe_json(self) -> dict[str, object]:
        payload: dict[str, object] = {"name": self.name, "instance": self.instance}
        if self.url is not None:
            payload["url"] = self.url
        if self.auth is not None:
            payload["auth"] = self.auth.to_safe_json()
        return payload


@dataclass(frozen=True)
class ProfileStore:
    """Loaded HotRepl profile file."""

    profiles: dict[str, Profile]

    @classmethod
    def load(cls, path: str | Path) -> Self:
        raw = Path(path).expanduser().read_text(encoding="utf-8")
        data = json.loads(raw)
        if not isinstance(data, dict):
            raise TypeError("Profile file must contain a JSON object.")
        profiles_obj = data.get("profiles")
        if not isinstance(profiles_obj, dict):
            raise TypeError("Profile file must contain a profiles object.")
        profiles = {
            str(name): Profile.from_json(str(name), profile_data)
            for name, profile_data in profiles_obj.items()
        }
        return cls(profiles)

    def require(self, name: str) -> Profile:
        try:
            return self.profiles[name]
        except KeyError as exc:
            raise KeyError(f"Profile '{name}' is missing.") from exc


def _optional_str(value: object) -> str | None:
    if value is None:
        return None
    return str(value)
