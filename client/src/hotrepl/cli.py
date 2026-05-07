"""CLI entry point for the hotrepl command."""

from __future__ import annotations

import argparse
import asyncio
from dataclasses import asdict, is_dataclass
import json
import os
import sys
from typing import Any

from hotrepl._client import DEFAULT_URL, Client, ControlCommandError, EvalError


def _get_url(args: argparse.Namespace) -> str:
    return args.url or os.environ.get("HOTREPL_URL", DEFAULT_URL)


async def _cmd_eval(args: argparse.Namespace) -> None:
    if args.file:
        with open(args.file) as f:
            code = f.read()
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
    async with Client(_get_url(args)) as client:
        try:
            if args.control_command == "describe":
                _print_json(await client.describe_commands())
            elif args.control_command == "call":
                _print_json(await client.call(args.name, _parse_json_object(args.args_json)))
            elif args.control_command == "start-job":
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
            _print_json({"error": asdict(exc.error), "diagnostics": [asdict(d) for d in exc.diagnostics]})
            sys.exit(1)


def _parse_json_object(raw: str) -> dict[str, Any]:
    parsed = json.loads(raw)
    if not isinstance(parsed, dict):
        raise SystemExit("control command args must be a JSON object")
    return parsed


def _print_json(value: Any) -> None:
    print(json.dumps(_jsonable(value), indent=2))


def _jsonable(value: Any) -> Any:
    if is_dataclass(value) and not isinstance(value, type):
        return asdict(value)
    if isinstance(value, list):
        return [_jsonable(item) for item in value]
    if isinstance(value, dict):
        return {key: _jsonable(item) for key, item in value.items()}
    return value

def _format_info(handshake: dict[str, Any]) -> str:
    evaluator = handshake.get("evaluator") or {}
    host = handshake.get("host") or {}
    available = handshake.get("availableEvaluators") or []
    language = evaluator.get(
        "languageVersion", handshake.get("csharpVersion", "unknown")
    )
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


def _cmd_test(args: argparse.Namespace) -> None:
    import subprocess
    from pathlib import Path

    tests_dir = Path(__file__).resolve().parent.parent.parent / "tests"

    env = os.environ.copy()
    env["HOTREPL_URL"] = args.url or os.environ.get("HOTREPL_URL", DEFAULT_URL)

    cmd = [sys.executable, "-m", "pytest", str(tests_dir)]
    if args.verbose:
        cmd.append("-v")
    if args.filter:
        cmd.extend(["-k", args.filter])

    result = subprocess.run(cmd, env=env)
    sys.exit(result.returncode)


def _build_parser() -> argparse.ArgumentParser:
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

    # control
    p_control = sub.add_parser("control", help="Typed control-plane commands")
    control_sub = p_control.add_subparsers(dest="control_command", required=True)

    control_sub.add_parser("describe", help="List registered control commands")

    p_control_call = control_sub.add_parser("call", help="Call a synchronous control command")
    p_control_call.add_argument("name", help="Control command name")
    p_control_call.add_argument("args_json", help="Command args JSON object")

    p_control_start = control_sub.add_parser("start-job", help="Start a job control command")
    p_control_start.add_argument("name", help="Control command name")
    p_control_start.add_argument("args_json", help="Command args JSON object")

    p_control_status = control_sub.add_parser("job-status", help="Get job status")
    p_control_status.add_argument("job_id", help="Job id")

    p_control_result = control_sub.add_parser("job-result", help="Get terminal job result")
    p_control_result.add_argument("job_id", help="Job id")

    p_control_cancel = control_sub.add_parser("cancel", help="Cancel a job")
    p_control_cancel.add_argument("job_id", help="Job id")

    # test
    p_test = sub.add_parser("test", help="Run smoke tests via pytest")
    p_test.add_argument("-v", "--verbose", action="store_true", help="Verbose output")
    p_test.add_argument("-k", "--filter", default=None, help="pytest -k filter expression")

    return parser


def main() -> None:
    parser = _build_parser()
    args = parser.parse_args()

    dispatch = {
        "eval": _cmd_eval,
        "ping": _cmd_ping,
        "reset": _cmd_reset,
        "complete": _cmd_complete,
        "watch": _cmd_watch,
        "info": _cmd_info,
        "select-evaluator": _cmd_select_evaluator,
        "control": _cmd_control,
    }

    if args.command == "test":
        _cmd_test(args)
        return

    handler = dispatch[args.command]
    try:
        asyncio.run(handler(args))
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
