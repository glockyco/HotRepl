#!/usr/bin/env python3
"""Reject unknown first-party typed commands in the operational HotRepl skill."""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "src/HotRepl.UnityCommands/UnityCommandCatalogNames.cs"
DEFAULT_SKILL = ROOT / ".claude/skills/hotrepl/SKILL.md"


def catalog_names() -> set[str]:
    text = CATALOG.read_text(encoding="utf-8")
    return set(re.findall(r'public const string \w+ = "([a-z0-9._-]+)";', text))


def example_names(text: str) -> list[str]:
    names = re.findall(r'"name"\s*:\s*"([a-z0-9._-]+)"', text)
    names.extend(re.findall(r"\bhotrepl (?:run|describe) ([a-z0-9._-]+)", text))
    return names


def main() -> int:
    paths = [Path(argument).resolve() for argument in sys.argv[1:]] or [DEFAULT_SKILL]
    registered = catalog_names()
    if not registered:
        print(f"No command names found in {CATALOG.relative_to(ROOT)}", file=sys.stderr)
        return 1

    errors: list[str] = []
    checked = 0
    for path in paths:
        text = path.read_text(encoding="utf-8")
        for name in sorted(example_names(text)):
            checked += 1
            if name not in registered:
                try:
                    display_path = path.relative_to(ROOT)
                except ValueError:
                    display_path = path
                errors.append(f"{display_path}: unknown first-party command {name}")

    if errors:
        print("command-example check failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print(f"command-example check passed: {checked} examples, {len(registered)} registered commands")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
