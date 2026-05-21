"""TypedDict definitions for the HotRepl WebSocket protocol messages.

These serve as documentation and type-checking, not runtime validation.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Required, TypedDict


class EvaluatorMetadata(TypedDict, total=False):
    name: str
    languageVersion: str
    supportsPersistentState: bool
    supportsCompletion: bool
    timeoutMode: str


class HostMetadata(TypedDict, total=False):
    name: str
    version: str
    runtime: str
    platform: str


class ControlPlaneLimits(TypedDict, total=False):
    maxMessageBytes: int
    maxInFlightCommands: int
    maxQueuedCommands: int
    maxJobEventBuffer: int


class ControlPlaneMetadata(TypedDict, total=False):
    supported: bool
    protocolVersion: int
    authRequired: bool
    leaseRequired: bool
    artifactRefsSupported: bool
    jobEventsSupported: bool
    jobEventReplaySupported: bool
    limits: ControlPlaneLimits


class Handshake(TypedDict, total=False):
    type: Required[str]
    version: str
    csharpVersion: str
    evaluator: EvaluatorMetadata
    host: HostMetadata
    availableEvaluators: list[str]
    defaultUsings: list[str]
    helpers: list[str]
    controlPlane: ControlPlaneMetadata


class EvalResult(TypedDict, total=False):
    type: Required[str]
    id: Required[str]
    hasValue: bool
    value: str | None
    valueType: str | None
    stdout: str
    durationMs: float


class EvalErrorResult(TypedDict, total=False):
    type: Required[str]
    id: Required[str]
    errorKind: str
    message: str
    stackTrace: str | None


class ResetResult(TypedDict):
    type: str
    id: str
    success: bool


class Pong(TypedDict):
    type: str
    id: str


class CompleteResult(TypedDict, total=False):
    type: Required[str]
    id: Required[str]
    completions: list[str]
    durationMs: float


class SubscribeResult(TypedDict, total=False):
    type: Required[str]
    id: Required[str]
    seq: int
    hasValue: bool
    value: str | None
    valueType: str | None
    durationMs: float
    final: bool


class SubscribeErrorResult(TypedDict, total=False):
    type: Required[str]
    id: Required[str]
    seq: int
    errorKind: str
    message: str
    final: bool


@dataclass(frozen=True)
class ControlError:
    kind: str
    code: str
    message: str
    retryable: bool
    details: dict[str, Any] | None = None


@dataclass(frozen=True)
class AuthResult:
    ok: bool
    session_id: str | None
    error: ControlError | None = None


@dataclass(frozen=True)
class LeaseResult:
    ok: bool
    lease_id: str | None
    error: ControlError | None = None


@dataclass(frozen=True)
class PreparedControl:
    auth: AuthResult | None
    lease: LeaseResult | None
    commands: list[ControlCommandDescriptor]


@dataclass(frozen=True)
class ControlCommandDescriptor:
    name: str
    version: int
    kind: str
    mutates_state: bool
    args_schema: dict[str, Any]
    result_schema: dict[str, Any]


@dataclass(frozen=True)
class ArtifactRef:
    logical_name: str
    uri: str
    path: str | None
    content_type: str
    byte_size: int
    sha256: str
    finalized: bool


@dataclass(frozen=True)
class CommandResult:
    status: str
    result: dict[str, Any]
    artifacts: list[ArtifactRef]
    diagnostics: list[ControlError]
    state: str | None = None


@dataclass(frozen=True)
class ControlRunTerminal:
    status: str
    result: dict[str, Any] | None = None
    artifacts: list[ArtifactRef] | None = None
    diagnostics: list[ControlError] | None = None
    error: ControlError | None = None


@dataclass(frozen=True)
class CommandAccepted:
    job_id: str
    state: str


@dataclass(frozen=True)
class JobStatus:
    job_id: str
    state: str
    progress: dict[str, Any] | None = None


@dataclass(frozen=True)
class JobCancelResult:
    accepted: bool
    state: str
