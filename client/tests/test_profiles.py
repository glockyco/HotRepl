from __future__ import annotations

import json
from typing import TYPE_CHECKING

import pytest
from hotrepl._profiles import ProfileStore

if TYPE_CHECKING:
    from pathlib import Path

pytestmark = pytest.mark.no_hotrepl_server


def test_profile_resolves_env_token_without_exposing_secret(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setenv("HOTREPL_TOKEN", "env-secret")
    profile_file = tmp_path / "profiles.json"
    profile_file.write_text(
        json.dumps(
            {
                "schemaVersion": 1,
                "profiles": {
                    "demo": {
                        "url": "ws://127.0.0.1:18590",
                        "auth": {"source": "env", "name": "HOTREPL_TOKEN"},
                    }
                },
            }
        ),
        encoding="utf-8",
    )

    profile = ProfileStore.load(profile_file).require("demo")

    assert profile.url == "ws://127.0.0.1:18590"
    assert profile.resolve_token() == "env-secret"
    assert "env-secret" not in json.dumps(profile.to_safe_json())
    auth = profile.to_safe_json()["auth"]
    assert isinstance(auth, dict)
    assert auth["source"] == "env"


def test_profile_resolves_token_file(tmp_path: Path) -> None:
    token_file = tmp_path / "token.txt"
    token_file.write_text("file-secret\n", encoding="utf-8")
    profile_file = tmp_path / "profiles.json"
    profile_file.write_text(
        json.dumps(
            {
                "schemaVersion": 1,
                "profiles": {
                    "demo": {
                        "auth": {"source": "token-file", "path": str(token_file)},
                    }
                },
            }
        ),
        encoding="utf-8",
    )

    profile = ProfileStore.load(profile_file).require("demo")

    assert profile.resolve_token() == "file-secret"


def test_profile_resolves_bepinex_config_key(tmp_path: Path) -> None:
    config_file = tmp_path / "hotrepl.bepinex.cfg"
    config_file.write_text("[Control]\nAuthToken = cfg-secret\n", encoding="utf-8")
    profile_file = tmp_path / "profiles.json"
    profile_file.write_text(
        json.dumps(
            {
                "schemaVersion": 1,
                "profiles": {
                    "demo": {
                        "auth": {
                            "source": "bepinex-config",
                            "path": str(config_file),
                            "section": "Control",
                            "key": "AuthToken",
                        }
                    }
                },
            }
        ),
        encoding="utf-8",
    )

    profile = ProfileStore.load(profile_file).require("demo")

    assert profile.resolve_token() == "cfg-secret"


def test_missing_profile_raises_clear_error(tmp_path: Path) -> None:
    profile_file = tmp_path / "profiles.json"
    profile_file.write_text('{"schemaVersion":1,"profiles":{}}', encoding="utf-8")

    with pytest.raises(KeyError, match="missing"):
        ProfileStore.load(profile_file).require("missing")
