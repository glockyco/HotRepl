#!/usr/bin/env python3
"""Write deterministic provenance for a completed HotRepl loader build."""

from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
from pathlib import Path


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def file_record(path: Path, root: Path | None = None) -> dict[str, object]:
    return {
        "path": str(path.relative_to(root) if root else path),
        "sha256": sha256(path),
        "byteSize": path.stat().st_size,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--loader", required=True, choices=("bepinex", "melonloader"))
    parser.add_argument("--configuration", required=True)
    parser.add_argument("--revision", required=True)
    parser.add_argument("--input", action="append", default=[], type=Path)
    args = parser.parse_args()

    outputs = sorted(
        path for path in args.output.rglob("*") if path.is_file() and path.name != "hotrepl-build.json"
    )
    manifest = {
        "schemaVersion": 1,
        "hotReplRevision": args.revision,
        "loader": args.loader,
        "configuration": args.configuration,
        "tools": {
            "dotnet": subprocess.check_output(["dotnet", "--version"], text=True).strip(),
        },
        "inputs": [file_record(path.resolve(), path.resolve().parent) for path in sorted(args.input)],
        "outputs": [file_record(path, args.output) for path in outputs],
    }
    destination = args.output / "hotrepl-build.json"
    destination.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n")


if __name__ == "__main__":
    main()
