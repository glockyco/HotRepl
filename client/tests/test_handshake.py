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


async def test_handshake_metadata_when_present(client: Client) -> None:
    assert client.handshake is not None
    evaluator = client.handshake.get("evaluator")
    if evaluator is not None:
        assert isinstance(evaluator.get("name"), str)
        assert isinstance(evaluator.get("languageVersion"), str)
        assert evaluator.get("timeoutMode") in {"HardAbort", "Cooperative", "None"}

    host = client.handshake.get("host")
    if host is not None:
        assert isinstance(host.get("name"), str)
        assert isinstance(host.get("runtime"), str)

    available = client.handshake.get("availableEvaluators")
    if available is not None:
        assert isinstance(available, list)
        assert all(isinstance(name, str) for name in available)


async def test_handshake_control_plane_metadata_when_present(client: Client) -> None:
    assert client.handshake is not None
    control_plane = client.handshake.get("controlPlane")
    if control_plane is None:
        return

    assert control_plane.get("supported") is True
    assert isinstance(control_plane.get("protocolVersion"), int)
    assert isinstance(control_plane.get("authRequired"), bool)
    assert isinstance(control_plane.get("leaseRequired"), bool)
    assert isinstance(control_plane.get("artifactRefsSupported"), bool)
    assert isinstance(control_plane.get("jobEventsSupported"), bool)
    limits = control_plane.get("limits")
    assert isinstance(limits, dict)
    assert isinstance(limits.get("maxMessageBytes"), int)
    assert isinstance(limits.get("maxInFlightCommands"), int)
    assert isinstance(limits.get("maxQueuedCommands"), int)
    assert isinstance(limits.get("maxJobEventBuffer"), int)
