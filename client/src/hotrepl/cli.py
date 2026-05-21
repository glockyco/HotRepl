"""CLI entry point for the hotrepl command."""

from __future__ import annotations

import argparse
import asyncio
import contextlib
import json
import os
import subprocess
import sys
import time
from dataclasses import asdict, is_dataclass
from pathlib import Path
from typing import TYPE_CHECKING, Any, cast

from hotrepl._client import (
    DEFAULT_URL,
    Client,
    ControlCommandError,
    EvalError,
    ServerUnreachableError,
)
from hotrepl._discovery import discover_instances
from hotrepl._output import (
    CliError,
    emit_json_error,
    emit_json_success,
    emit_jsonl_event,
    exit_code_for_error,
)
from hotrepl._profiles import AuthSource, Profile, ProfileStore, default_profile_path
from hotrepl._types import ControlError

if TYPE_CHECKING:
    from hotrepl._types import ControlRunTerminal


def _get_url(args: argparse.Namespace) -> str:
    profile = _load_profile(args)
    if profile is not None and profile.url is not None:
        return profile.url
    return args.url or os.environ.get("HOTREPL_URL", DEFAULT_URL)


def _get_token_source(args: argparse.Namespace) -> str | AuthSource | None:
    token = getattr(args, "token", None)
    if token:
        return AuthSource("token", key=token)
    profile = _load_profile(args)
    if profile is not None:
        return profile.auth
    return None


def _load_profile(args: argparse.Namespace) -> Profile | None:
    profile_name = getattr(args, "profile", None)
    if not profile_name:
        return None
    profile_file = getattr(args, "profile_file", None)
    path = profile_file or default_profile_path()
    return ProfileStore.load(path).require(profile_name)


async def _cmd_eval(args: argparse.Namespace) -> None:
    if args.file:
        code = await asyncio.to_thread(Path(args.file).read_text)
    else:
        code = args.code

    async with Client(_get_url(args)) as client:
        try:
            result = await client.eval(code, timeout_ms=args.timeout)
        except EvalError as e:
            if args.json:
                print(json.dumps({"error": str(e), "kind": e.kind, "stackTrace": e.stack_trace}))
                sys.exit(1)
            if e.kind == "compile":
                print(f"Compile error: {e}", file=sys.stderr)
            elif e.kind == "runtime":
                print(f"Runtime error: {e}", file=sys.stderr)
                if e.stack_trace:
                    print(e.stack_trace, file=sys.stderr)
            else:
                print(f"Error ({e.kind}): {e}", file=sys.stderr)
            sys.exit(1)

    if args.json:
        print(json.dumps(result, indent=2))
    else:
        if result.get("stdout"):
            sys.stderr.write(result["stdout"])
        if result.get("hasValue"):
            print(result.get("value", ""))


async def _cmd_ping(args: argparse.Namespace) -> None:
    async with Client(_get_url(args)) as client:
        ms = await client.ping()

    if args.json:
        print(json.dumps({"ms": ms}))
    else:
        print(f"pong: {ms:.1f}ms")


async def _cmd_reset(args: argparse.Namespace) -> None:
    async with Client(_get_url(args)) as client:
        result = await client.reset()

    if args.json:
        print(json.dumps(result, indent=2))
    else:
        print("ok" if result.get("success") else "failed")


async def _cmd_complete(args: argparse.Namespace) -> None:
    async with Client(_get_url(args)) as client:
        completions = await client.complete(args.code, cursor_pos=args.cursor)

    if args.json:
        print(json.dumps(completions, indent=2))
    else:
        for c in completions:
            print(c)


async def _cmd_info(args: argparse.Namespace) -> None:
    async with Client(_get_url(args)) as client:
        handshake = client.handshake or {}

    if args.json:
        print(json.dumps(handshake, indent=2))
    else:
        print(_format_info(handshake))


async def _cmd_control(args: argparse.Namespace) -> None:
    if args.control_command == "run":
        await _cmd_control_run(args)
        return

    async with Client(_get_url(args)) as client:
        try:
            token_source = _get_token_source(args)
            if args.control_command == "describe":
                prepared = await client.prepare_control(token_source=token_source)
                _print_json(prepared.commands)
            elif args.control_command == "call":
                await client.prepare_control(
                    token_source=token_source, require_commands=[args.name]
                )
                _print_json(await client.call(args.name, _parse_json_object(args.args_json)))
            elif args.control_command == "start-job":
                await client.prepare_control(
                    token_source=token_source, require_commands=[args.name]
                )
                _print_json(await client.start_job(args.name, _parse_json_object(args.args_json)))
            elif args.control_command == "job-status":
                _print_json(await client.job_status(args.job_id))
            elif args.control_command == "job-result":
                _print_json(await client.job_result(args.job_id))
            elif args.control_command == "cancel":
                _print_json(await client.cancel_job(args.job_id))
            else:
                raise RuntimeError(f"Unknown control command: {args.control_command}")
        except ControlCommandError as exc:
            _print_json(
                {
                    "error": asdict(exc.error),
                    "diagnostics": [asdict(d) for d in exc.diagnostics],
                }
            )
            sys.exit(1)


async def _cmd_control_run(args: argparse.Namespace) -> None:
    async with Client(_get_url(args)) as client:
        try:
            prepared = await client.prepare_control(
                token_source=_get_token_source(args),
                client_name=args.client_name,
                acquire_lease=args.lease,
                require_commands=[args.name],
            )
            mutates_state = _descriptor_mutates(prepared.commands, args.name)
            if args.jsonl:
                emit_jsonl_event("hotrepl.control.run", "connect", "completed")

            def on_progress(status: Any) -> None:
                if args.jsonl and status.progress:
                    emit_jsonl_event(
                        "hotrepl.control.run",
                        "job",
                        status.state,
                        progress=status.progress,
                    )

            terminal = await client.run_control_job(
                args.name,
                _parse_json_object(args.args_json),
                wait=args.wait,
                poll_interval_s=args.poll_interval,
                interrupt_grace_s=args.interrupt_grace,
                mutates_state=mutates_state,
                on_progress=on_progress,
            )
        except ControlCommandError as exc:
            error = _cli_error_from_control(exc.error)
            if args.jsonl:
                emit_jsonl_event(
                    "hotrepl.control.run",
                    "complete",
                    "failed",
                    error=error,
                    diagnostics=exc.diagnostics,
                )
                raise SystemExit(exit_code_for_error(error)) from None
            raise SystemExit(emit_json_error("hotrepl.control.run", error)) from None

    if args.jsonl:
        _emit_terminal_event(terminal)
    else:
        _print_json(terminal)
    raise SystemExit(_terminal_exit_code(terminal))


def _descriptor_mutates(commands: list[Any], name: str) -> bool:
    return any(command.name == name and command.mutates_state for command in commands)


def _emit_terminal_event(terminal: ControlRunTerminal) -> None:
    fields: dict[str, Any] = {}
    if terminal.result is not None:
        fields["result"] = terminal.result
    if terminal.diagnostics is not None:
        fields["diagnostics"] = terminal.diagnostics
    if terminal.artifacts is not None:
        fields["artifacts"] = terminal.artifacts
    if terminal.error is not None:
        fields["error"] = _cli_error_from_control(terminal.error)
    emit_jsonl_event("hotrepl.control.run", "complete", terminal.status, **fields)


def _terminal_exit_code(terminal: ControlRunTerminal) -> int:
    if terminal.status == "completed":
        return 0
    if terminal.status == "cancelled":
        return 8
    if terminal.status == "interrupted":
        return 9
    if terminal.status == "abandoned":
        return 10
    return 7


def _cli_error_from_control(error: ControlError) -> CliError:
    return CliError(
        kind=error.kind,
        code=error.code,
        message=error.message,
        retryable=error.retryable,
        details=error.details,
    )


def _parse_json_object(raw: str) -> dict[str, Any]:
    parsed = json.loads(raw)
    if not isinstance(parsed, dict):
        raise SystemExit("control command args must be a JSON object")
    return cast("dict[str, Any]", parsed)


def _print_json(value: Any) -> None:
    print(json.dumps(_jsonable(value), indent=2))


def _jsonable(value: object) -> object:
    if is_dataclass(value) and not isinstance(value, type):
        return asdict(value)
    if isinstance(value, list):
        items = cast("list[object]", value)
        return [_jsonable(item) for item in items]
    if isinstance(value, dict):
        mapping = cast("dict[str, object]", value)
        return {key: _jsonable(item) for key, item in mapping.items()}
    return value


def _format_info(handshake: dict[str, Any]) -> str:
    evaluator = cast("dict[str, Any]", handshake.get("evaluator") or {})
    host = cast("dict[str, Any]", handshake.get("host") or {})
    available = cast("list[str]", handshake.get("availableEvaluators") or [])
    language = evaluator.get("languageVersion", handshake.get("csharpVersion", "unknown"))
    return "\n".join(
        [
            f"host: {host.get('name', 'unknown')} {host.get('version', '')}".rstrip(),
            f"runtime: {host.get('runtime', 'unknown')}",
            f"platform: {host.get('platform', 'unknown')}",
            f"evaluator: {evaluator.get('name', 'unknown')}",
            f"language: {language}",
            f"timeout: {evaluator.get('timeoutMode', 'unknown')}",
            "available evaluators: " + ", ".join(available),
        ]
    )


async def _cmd_select_evaluator(args: argparse.Namespace) -> None:
    async with Client(_get_url(args)) as client:
        result = await client.select_evaluator(args.evaluator)

    if args.json:
        print(json.dumps(result, indent=2))
    else:
        print(f"selected: {result.get('evaluator')}")


async def _cmd_watch(args: argparse.Namespace) -> None:
    async with Client(_get_url(args)) as client:
        gen = client.subscribe(
            args.code,
            interval_frames=args.interval,
            on_change=args.on_change,
            limit=args.limit,
            timeout_ms=args.timeout,
        )
        async for msg in gen:
            if args.json:
                print(json.dumps(msg))
            else:
                _print_subscribe_msg(msg)


def _print_subscribe_msg(msg: dict[str, Any]) -> None:
    if "errorKind" in msg:
        print(f"[{msg.get('seq', '?')}] ERROR ({msg['errorKind']}): {msg.get('message', '')}")
    elif msg.get("hasValue"):
        print(f"[{msg.get('seq', '?')}] {msg.get('value', '')}")
    else:
        print(f"[{msg.get('seq', '?')}] (void)")


async def _cmd_status(args: argparse.Namespace) -> None:
    required = _required_commands(args)
    try:
        async with Client(_get_url(args)) as client:
            data = await _collect_readiness(
                client,
                token_source=_get_token_source(args),
                required_commands=required,
                acquire_lease=False,
                client_name=getattr(args, "client_name", "hotrepl-cli"),
            )
    except ServerUnreachableError as exc:
        data = _unreachable_readiness(str(exc), required)

    if args.json:
        raise SystemExit(emit_json_success("hotrepl.status", data))
    _print_readiness(data)
    raise SystemExit(0)


async def _cmd_doctor(args: argparse.Namespace) -> None:
    required = _required_commands(args)
    try:
        async with Client(_get_url(args)) as client:
            data = await _collect_readiness(
                client,
                token_source=_get_token_source(args),
                required_commands=required,
                acquire_lease=getattr(args, "lease", False),
                client_name=getattr(args, "client_name", "hotrepl-cli"),
            )
    except ServerUnreachableError as exc:
        data = _unreachable_readiness(str(exc), required)

    if args.json:
        raise SystemExit(emit_json_success("hotrepl.doctor", data))
    _print_readiness(data)
    raise SystemExit(0)


async def _cmd_wait(args: argparse.Namespace) -> None:
    required = _required_commands(args)
    deadline = time.monotonic() + args.timeout
    try:
        async with Client(_get_url(args)) as client:
            while True:
                data = await _collect_readiness(
                    client,
                    token_source=_get_token_source(args),
                    required_commands=required,
                    acquire_lease=args.lease,
                    client_name=args.client_name,
                )
                if data["ready"]:
                    if args.json:
                        raise SystemExit(emit_json_success("hotrepl.wait", data))
                    _print_readiness(data)
                    raise SystemExit(0)
                blocking_error = data.get("error")
                if isinstance(blocking_error, CliError):
                    raise SystemExit(emit_json_error("hotrepl.wait", blocking_error))
                if time.monotonic() >= deadline:
                    error = CliError(
                        "timeout",
                        "readinessTimeout",
                        "Timed out waiting for HotRepl readiness.",
                        True,
                        details={"readiness": data},
                    )
                    raise SystemExit(emit_json_error("hotrepl.wait", error))
                await asyncio.sleep(args.interval)
    except ServerUnreachableError as exc:
        error = CliError("server_unreachable", "serverUnreachable", str(exc), True)
        raise SystemExit(emit_json_error("hotrepl.wait", error)) from None


async def _collect_readiness(
    client: Client,
    *,
    token_source: str | AuthSource | None,
    required_commands: list[str],
    acquire_lease: bool,
    client_name: str,
) -> dict[str, object]:
    checks = [
        _check(
            "socket.connect",
            "pass",
            observed={"url": client.url},
        )
    ]
    control_plane = cast("dict[str, Any]", (client.handshake or {}).get("controlPlane") or {})
    if not bool(control_plane.get("supported", False)):
        checks.extend(
            [
                _check("control.supported", "fail", remediation="Load a HotRepl control host."),
                _check("control.describe", "blocked"),
                _check("commands.required", "unobserved", observed={"required": required_commands}),
            ]
        )
        return _readiness_payload(checks)

    checks.append(_check("control.supported", "pass", observed=control_plane))
    token = _resolve_token_source(token_source)
    if (bool(control_plane.get("authRequired")) or token is not None) and client.session_id is None:
        auth = await client.authenticate(token)
        if not auth.ok:
            checks.extend(
                [
                    _check(
                        "control.auth",
                        "fail",
                        observed={"required": bool(control_plane.get("authRequired"))},
                        remediation="Verify the selected HotRepl control auth source.",
                    ),
                    _check("control.describe", "blocked"),
                    _check(
                        "commands.required",
                        "unobserved",
                        observed={"required": required_commands},
                    ),
                ]
            )
            error = _cli_error_from_control(
                auth.error
                or ControlError(
                    "auth_failed",
                    "authFailed",
                    "Control authentication failed.",
                    False,
                )
            )
            return _readiness_payload(checks, error=error)
    checks.append(
        _check(
            "control.auth",
            "pass",
            observed={
                "required": bool(control_plane.get("authRequired")),
                "authenticated": client.session_id is not None,
            },
        )
    )

    commands = await client.describe_commands()
    checks.append(_check("control.describe", "pass", observed={"count": len(commands)}))
    command_check = _commands_check(commands, required_commands)
    checks.append(command_check)
    if command_check["status"] != "pass":
        if acquire_lease:
            checks.append(_check("control.lease", "blocked", acquires_lease=True))
        return _readiness_payload(checks)

    if acquire_lease:
        if client.lease_id is None:
            lease = await client.acquire_lease(client_name)
            if not lease.ok:
                checks.append(
                    _check(
                        "control.lease",
                        "fail",
                        observed={"leaseRequired": bool(control_plane.get("leaseRequired"))},
                        remediation="Release the conflicting lease or retry after it expires.",
                        acquires_lease=True,
                    )
                )
                error = _cli_error_from_control(
                    lease.error
                    or ControlError(
                        "lease_conflict",
                        "leaseAcquisitionFailed",
                        "Control lease acquisition failed.",
                        True,
                    )
                )
                return _readiness_payload(checks, error=error)
        checks.append(
            _check(
                "control.lease",
                "pass",
                observed={
                    "leaseRequired": bool(control_plane.get("leaseRequired")),
                    "heldByCaller": True,
                },
                acquires_lease=True,
            )
        )

    return _readiness_payload(checks)


def _commands_check(commands: list[Any], required_commands: list[str]) -> dict[str, object]:
    available = [command.name for command in commands]
    missing = [name for name in required_commands if name not in available]
    if missing:
        return _check(
            "commands.required",
            "fail",
            observed={"required": required_commands, "available": available, "missing": missing},
            remediation="Verify the game-specific plugin registered its control commands.",
        )
    return _check(
        "commands.required",
        "pass",
        observed={"required": required_commands, "available": available},
    )


def _readiness_payload(
    checks: list[dict[str, object]], *, error: CliError | None = None
) -> dict[str, object]:
    payload: dict[str, object] = {
        "ready": all(check["status"] == "pass" for check in checks),
        "connectionImpact": _connection_impact(False),
        "checks": checks,
    }
    if error is not None:
        payload["error"] = error
    return payload


def _unreachable_readiness(message: str, required_commands: list[str]) -> dict[str, object]:
    return _readiness_payload(
        [
            _check(
                "socket.connect",
                "fail",
                observed={"error": message},
                remediation="Start the game and verify HotRepl is loaded.",
            ),
            _check("control.describe", "blocked"),
            _check("commands.required", "unobserved", observed={"required": required_commands}),
        ]
    )


def _check(
    name: str,
    status: str,
    *,
    severity: str = "required",
    retryable: bool = True,
    observed: dict[str, object] | None = None,
    remediation: str | None = None,
    acquires_lease: bool = False,
) -> dict[str, object]:
    payload: dict[str, object] = {
        "name": name,
        "status": status,
        "severity": severity,
        "retryable": retryable,
        "connectionImpact": _connection_impact(acquires_lease),
    }
    if observed is not None:
        payload["observed"] = observed
    if remediation is not None:
        payload["remediation"] = remediation
    return payload


def _connection_impact(acquires_lease: bool) -> dict[str, object]:
    return {
        "mode": "active-websocket",
        "mayReplaceActiveClient": True,
        "acquiresLease": acquires_lease,
    }


def _required_commands(args: argparse.Namespace) -> list[str]:
    raw = getattr(args, "commands", None)
    if not raw:
        return []
    return [part.strip() for part in raw.split(",") if part.strip()]


def _resolve_token_source(token_source: str | AuthSource | None) -> str | None:
    if token_source is None:
        return None
    if isinstance(token_source, str):
        return token_source
    return token_source.resolve_token()


def _print_readiness(data: dict[str, object]) -> None:
    state = "ready" if data["ready"] else "not ready"
    print(state)


async def _cmd_discover(args: argparse.Namespace) -> None:
    roots = [args.instances_dir] if args.instances_dir else None
    profile = _load_profile(args)
    result = discover_instances(
        roots,
        host=args.host,
        instance_filter=profile.instance if profile is not None else None,
    )
    payload = result.to_json()
    if args.profile:
        payload["profile"] = args.profile
    if args.json:
        raise SystemExit(emit_json_success("hotrepl.discover", payload))

    for instance in result.instances:
        print(f"{instance.instance_id} {instance.url}")
    for diagnostic in result.diagnostics:
        print(diagnostic.message, file=sys.stderr)
    if not result.instances and result.diagnostics:
        raise SystemExit(emit_json_error("hotrepl.discover", result.diagnostics[0]))


def _add_discover_parser(sub: Any) -> None:
    p_discover = sub.add_parser("discover", help="Discover local HotRepl instance documents")
    p_discover.add_argument("--host", default=None, help="Filter by host adapter name")
    p_discover.add_argument("--profile", default=None, help="Profile name for instance filters")
    p_discover.add_argument("--profile-file", default=None, help="HotRepl profile JSON file")
    p_discover.add_argument(
        "--instances-dir", default=None, help="Override instance document directory"
    )
    p_discover.add_argument("--json", action="store_true", help="Output JSON envelope")


def _add_profile_args(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--profile", default=None, help="Profile name")
    parser.add_argument("--profile-file", default=None, help="HotRepl profile JSON file")
    parser.add_argument("--token", default=None, help="Unsafe explicit control auth token")
    parser.add_argument("--client-name", default="hotrepl-cli", help="Control lease client name")


def _add_readiness_parsers(sub: Any) -> None:
    p_status = sub.add_parser("status", help="Report HotRepl readiness without waiting")
    _add_profile_args(p_status)
    p_status.add_argument("--commands", default=None, help="Comma-separated required commands")
    p_status.add_argument("--json", action="store_true", help="Output JSON envelope")

    p_wait = sub.add_parser("wait", help="Wait for HotRepl readiness")
    _add_profile_args(p_wait)
    p_wait.add_argument("--lease", action="store_true", help="Acquire a control lease")
    p_wait.add_argument("--commands", default=None, help="Comma-separated required commands")
    p_wait.add_argument("--timeout", type=float, default=30.0, help="Readiness timeout in seconds")
    p_wait.add_argument("--interval", type=float, default=1.0, help="Retry interval in seconds")
    p_wait.add_argument("--json", action="store_true", help="Output JSON envelope")

    p_doctor = sub.add_parser("doctor", help="Report all observable HotRepl readiness checks")
    _add_profile_args(p_doctor)
    p_doctor.add_argument("--lease", action="store_true", help="Acquire a control lease")
    p_doctor.add_argument("--commands", default=None, help="Comma-separated required commands")
    p_doctor.add_argument("--json", action="store_true", help="Output JSON envelope")


def _add_control_parser(sub: Any) -> None:
    p_control = sub.add_parser("control", help="Typed control-plane commands")
    control_sub = p_control.add_subparsers(dest="control_command", required=True)

    p_control_describe = control_sub.add_parser("describe", help="List registered control commands")
    _add_profile_args(p_control_describe)

    p_control_call = control_sub.add_parser("call", help="Call a synchronous control command")
    p_control_call.add_argument("name", help="Control command name")
    p_control_call.add_argument("args_json", help="Command args JSON object")
    _add_profile_args(p_control_call)

    p_control_start = control_sub.add_parser("start-job", help="Start a job control command")
    p_control_start.add_argument("name", help="Control command name")
    p_control_start.add_argument("args_json", help="Command args JSON object")
    _add_profile_args(p_control_start)

    p_control_run = control_sub.add_parser("run", help="Run and optionally wait for a job command")
    p_control_run.add_argument("name", help="Control command name")
    p_control_run.add_argument("args_json", help="Command args JSON object")
    _add_profile_args(p_control_run)
    p_control_run.add_argument("--lease", action="store_true", help="Acquire a control lease")
    p_control_run.add_argument("--wait", action="store_true", help="Wait for terminal job state")
    p_control_run.add_argument("--jsonl", action="store_true", help="Output JSON Lines events")
    p_control_run.add_argument(
        "--poll-interval", type=float, default=0.5, help="Job poll interval in seconds"
    )
    p_control_run.add_argument(
        "--interrupt-grace", type=float, default=5.0, help="Cancellation grace period in seconds"
    )

    p_control_status = control_sub.add_parser("job-status", help="Get job status")
    p_control_status.add_argument("job_id", help="Job id")

    p_control_result = control_sub.add_parser("job-result", help="Get terminal job result")
    p_control_result.add_argument("job_id", help="Job id")

    p_control_cancel = control_sub.add_parser("cancel", help="Cancel a job")
    p_control_cancel.add_argument("job_id", help="Job id")


def _cmd_test(args: argparse.Namespace) -> None:
    """Invoke pytest against the bundled smoke tests."""
    tests_dir = Path(__file__).resolve().parent.parent.parent / "tests"

    env = os.environ.copy()
    env["HOTREPL_URL"] = args.url or os.environ.get("HOTREPL_URL", DEFAULT_URL)

    cmd = [sys.executable, "-m", "pytest", str(tests_dir)]
    if args.verbose:
        cmd.append("-v")
    if args.filter:
        cmd.extend(["-k", args.filter])

    # Trusted args: cmd is built from sys.executable and literal pytest flags.
    result = subprocess.run(cmd, env=env, check=False)  # noqa: S603
    sys.exit(result.returncode)


def build_parser() -> argparse.ArgumentParser:
    """Build the top-level argparse parser for the hotrepl CLI."""
    parser = argparse.ArgumentParser(
        prog="hotrepl",
        description="HotRepl C# REPL client",
    )
    parser.add_argument(
        "--url",
        default=None,
        help=f"WebSocket URL (env: HOTREPL_URL, default: {DEFAULT_URL})",
    )

    sub = parser.add_subparsers(dest="command", required=True)

    # eval
    p_eval = sub.add_parser("eval", help="Evaluate C# code")
    p_eval.add_argument("code", nargs="?", default=None, help="C# code to evaluate")
    p_eval.add_argument("--file", "-f", default=None, help="Read code from file")
    p_eval.add_argument("--timeout", "-t", type=int, default=10000, help="Timeout in ms")
    p_eval.add_argument("--json", action="store_true", help="Output raw JSON")

    # ping
    p_ping = sub.add_parser("ping", help="Ping the server")
    p_ping.add_argument("--json", action="store_true", help="Output raw JSON")

    # reset
    p_reset = sub.add_parser("reset", help="Reset REPL state")
    p_reset.add_argument("--json", action="store_true", help="Output raw JSON")

    # complete
    p_complete = sub.add_parser("complete", help="Autocomplete C# code")
    p_complete.add_argument("code", help="C# code to complete")
    p_complete.add_argument("--cursor", type=int, default=-1, help="Cursor position")
    p_complete.add_argument("--json", action="store_true", help="Output raw JSON")

    # info
    p_info = sub.add_parser("info", help="Show server host and evaluator metadata")
    p_info.add_argument("--json", action="store_true", help="Output raw JSON")

    # select-evaluator
    p_select = sub.add_parser("select-evaluator", help="Select server evaluator")
    p_select.add_argument("evaluator", help="Evaluator name from handshake.availableEvaluators")
    p_select.add_argument("--json", action="store_true", help="Output raw JSON")

    # watch
    p_watch = sub.add_parser("watch", help="Subscribe to repeated evaluation")
    p_watch.add_argument("code", help="C# code to evaluate")
    p_watch.add_argument("--interval", type=int, default=1, help="Interval in frames")
    p_watch.add_argument("--on-change", action="store_true", help="Only emit on value change")
    p_watch.add_argument("--limit", type=int, default=0, help="Max iterations (0=unlimited)")
    p_watch.add_argument("--timeout", "-t", type=int, default=10000, help="Timeout per eval in ms")
    p_watch.add_argument("--json", action="store_true", help="Output raw JSON")

    _add_discover_parser(sub)
    _add_readiness_parsers(sub)
    _add_control_parser(sub)

    # test
    p_test = sub.add_parser("test", help="Run smoke tests via pytest")
    p_test.add_argument("-v", "--verbose", action="store_true", help="Verbose output")
    p_test.add_argument("-k", "--filter", default=None, help="pytest -k filter expression")

    return parser


def main() -> None:
    """Dispatch the parsed CLI command to its handler."""
    parser = build_parser()
    args = parser.parse_args()

    dispatch = {
        "eval": _cmd_eval,
        "ping": _cmd_ping,
        "reset": _cmd_reset,
        "complete": _cmd_complete,
        "watch": _cmd_watch,
        "info": _cmd_info,
        "select-evaluator": _cmd_select_evaluator,
        "discover": _cmd_discover,
        "status": _cmd_status,
        "wait": _cmd_wait,
        "doctor": _cmd_doctor,
        "control": _cmd_control,
    }

    if args.command == "test":
        _cmd_test(args)
        return

    handler = dispatch[args.command]
    with contextlib.suppress(KeyboardInterrupt):
        asyncio.run(handler(args))


if __name__ == "__main__":
    main()
