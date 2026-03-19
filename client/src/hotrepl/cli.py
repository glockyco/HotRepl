"""CLI entry point for the hotrepl command."""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import sys
from typing import Any

from hotrepl._client import DEFAULT_URL, Client


def _get_url(args: argparse.Namespace) -> str:
    return args.url or os.environ.get("HOTREPL_URL", DEFAULT_URL)


async def _cmd_eval(args: argparse.Namespace) -> None:
    if args.file:
        with open(args.file) as f:
            code = f.read()
    else:
        code = args.code

    async with Client(_get_url(args)) as client:
        result = await client.eval(code, timeout_ms=args.timeout)

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

    # watch
    p_watch = sub.add_parser("watch", help="Subscribe to repeated evaluation")
    p_watch.add_argument("code", help="C# code to evaluate")
    p_watch.add_argument("--interval", type=int, default=1, help="Interval in frames")
    p_watch.add_argument("--on-change", action="store_true", help="Only emit on value change")
    p_watch.add_argument("--limit", type=int, default=0, help="Max iterations (0=unlimited)")
    p_watch.add_argument("--timeout", "-t", type=int, default=10000, help="Timeout per eval in ms")
    p_watch.add_argument("--json", action="store_true", help="Output raw JSON")

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
