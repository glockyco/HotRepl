"""Tests for the server handshake message received on connect."""

from __future__ import annotations

import pytest

from hotrepl import Client

pytestmark = pytest.mark.asyncio


async def test_handshake_type(client: Client) -> None:
    assert client.handshake is not None
    assert client.handshake["type"] == "handshake"


async def test_handshake_has_version(client: Client) -> None:
    assert client.handshake is not None
    version = client.handshake.get("version")
    assert isinstance(version, str)
    assert len(version) > 0


async def test_handshake_has_csharp_version(client: Client) -> None:
    assert client.handshake is not None
    assert "csharpVersion" in client.handshake


async def test_handshake_has_default_usings(client: Client) -> None:
    assert client.handshake is not None
    usings = client.handshake.get("defaultUsings")
    assert isinstance(usings, list)
    assert len(usings) > 0


async def test_handshake_has_helpers(client: Client) -> None:
    assert client.handshake is not None
    helpers = client.handshake.get("helpers")
    assert isinstance(helpers, list)
