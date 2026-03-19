"""TypedDict definitions for the HotRepl WebSocket protocol messages.

These serve as documentation and type-checking, not runtime validation.
"""

from __future__ import annotations

from typing import Required, TypedDict


class Handshake(TypedDict):
    type: str
    version: str
    csharpVersion: str
    defaultUsings: list[str]
    helpers: list[str]


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
