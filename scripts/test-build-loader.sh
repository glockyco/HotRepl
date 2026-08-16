#!/usr/bin/env bash
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
temporary=$(mktemp -d "${TMPDIR:-/tmp}/hotrepl-loader-test.XXXXXX")
trap 'rm -rf "$temporary"' EXIT

mkdir -p "$temporary/assemblies" "$temporary/result"
printf 'unity' > "$temporary/assemblies/UnityEngine.dll"

if HOTREPL_SOURCE="$repo_root" HOTREPL_REVISION=test \
  "$repo_root/scripts/build-loader.sh" \
  --loader bepinex \
  --assemblies "$temporary/assemblies" \
  --output "$temporary/missing-output" >/dev/null 2>&1; then
  echo "Expected a missing UnityEngine.CoreModule.dll to fail." >&2
  exit 1
fi
[[ ! -e "$temporary/missing-output" ]]

printf 'input' > "$temporary/input.dll"
printf 'output' > "$temporary/result/HotRepl.dll"
python3 "$repo_root/scripts/write-build-manifest.py" \
  --output "$temporary/result" \
  --loader bepinex \
  --configuration Release \
  --revision test-revision \
  --input "$temporary/input.dll"

python3 -c '
import json
import sys
from pathlib import Path
manifest = json.loads(Path(sys.argv[1]).read_text())
assert manifest["schemaVersion"] == 1
assert manifest["hotReplRevision"] == "test-revision"
assert manifest["loader"] == "bepinex"
assert manifest["inputs"][0]["path"] == Path(sys.argv[2]).name
assert manifest["outputs"][0]["path"] == "HotRepl.dll"
assert len(manifest["inputs"][0]["sha256"]) == 64
assert len(manifest["outputs"][0]["sha256"]) == 64
' "$temporary/result/hotrepl-build.json" "$temporary/input.dll"
