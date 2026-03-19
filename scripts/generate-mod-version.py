#!/usr/bin/env python3
"""Generate mod version from git using CalVer format.

Format: YYYY.MDD.0

The version uses the date of the latest git commit touching the mod
directory. The third component is always 0 when generated locally;
the publish command overrides it with an auto-incremented revision
from the Thunderstore API.

Example:
  $ python3 scripts/generate-mod-version.py src/mods/JusticeForF7
  2026.213.0

For Release builds, fails if working tree has uncommitted changes.
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path


def get_commit_date(mod_dir: Path) -> tuple[int, int, int] | None:
    """Get the date of the latest commit touching a directory.

    Returns:
        Tuple of (year, month, day) or None if git fails
    """
    try:
        date_str = subprocess.run(
            ["git", "log", "-1", "--format=%cs", mod_dir],
            capture_output=True,
            text=True,
            check=True,
        ).stdout.strip()

        if not date_str:
            return None

        year, month, day = date_str.split("-")
        return (int(year), int(month), int(day))
    except (subprocess.CalledProcessError, ValueError):
        return None


def check_dirty_tree(mod_dir: Path) -> bool:
    """Check if working tree has uncommitted changes."""
    try:
        result = subprocess.run(
            ["git", "status", "--porcelain", mod_dir],
            capture_output=True,
            text=True,
            check=True,
        )
        return bool(result.stdout.strip())
    except subprocess.CalledProcessError:
        return False


def get_version(mod_dir: Path, config: str = "Debug") -> str:
    """Generate version for a mod in YYYY.MDD.0 format.

    Args:
        mod_dir: Path to mod directory (relative to repo root)
        config: Build configuration (Debug or Release)

    Returns:
        Version string in format YYYY.MDD.0
    """
    date = get_commit_date(mod_dir)

    if not date:
        return "0.0.0"

    year, month, day = date

    if check_dirty_tree(mod_dir) and config == "Release":
        print(
            f"error: Cannot build Release with uncommitted changes in {mod_dir}",
            file=sys.stderr,
        )
        print("Commit or stash your changes first.", file=sys.stderr)
        sys.exit(1)

    return f"{year}.{month}{day:02d}.0"


def main() -> None:
    """Main entry point."""
    if len(sys.argv) < 2:
        print("Usage: generate-mod-version.py <mod_dir> [Debug|Release]", file=sys.stderr)
        sys.exit(1)

    mod_dir = Path(sys.argv[1])
    config = sys.argv[2] if len(sys.argv) > 2 else "Debug"

    version = get_version(mod_dir, config)
    print(version)


if __name__ == "__main__":
    main()
