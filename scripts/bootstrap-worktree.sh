#!/usr/bin/env bash
# Bootstrap a fresh HotRepl git worktree.
#
# Fresh git worktrees contain tracked files only. HotRepl additionally requires
# gitignored inputs (Unity DLLs in `lib/`, BepInEx assemblies, `Local.props`)
# and per-worktree restored state (`.dotnet/tools/`, `node_modules/`) before
# builds and tests are meaningful.
#
# This script never overwrites tracked files. Optional gitignored inputs are
# linked (symlink) from a trusted source checkout so multiple worktrees share
# one local copy of large Unity DLLs.
#
# Usage:
#   scripts/bootstrap-worktree.sh [--source <trusted-checkout>]
#
# Run from inside a HotRepl git worktree.

set -euo pipefail

usage() {
  cat <<'USAGE'
Usage: scripts/bootstrap-worktree.sh [options]

Bootstrap a fresh HotRepl git worktree:
  - link gitignored Unity DLLs from a trusted checkout (optional)
  - link `Local.props` if the source has one and the worktree does not
  - run `dotnet tool restore` (csharpier, etc.)
  - run `bun install --frozen-lockfile`
  - report what is still missing

Options:
  --source <path>   Trusted local checkout to link gitignored inputs from.
                    Skip to bootstrap a Core-only worktree (no host build).
  -h, --help        Show this help and exit.
USAGE
}

source_checkout=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --source)
      [[ $# -ge 2 ]] || { echo "Error: --source requires a path" >&2; exit 2; }
      source_checkout="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Error: unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

repo_root=$(git rev-parse --show-toplevel 2>/dev/null) || {
  echo "Error: not inside a HotRepl git worktree." >&2
  exit 1
}
repo_root=$(cd "$repo_root" && pwd -P)

# Refuse to bootstrap a checkout that does not look like HotRepl.
if [[ ! -f "$repo_root/src/HotRepl.Core/HotRepl.Core.csproj" ]]; then
  echo "Error: $repo_root does not look like a HotRepl checkout (missing HotRepl.Core.csproj)." >&2
  exit 1
fi

if [[ -n "$source_checkout" ]]; then
  if [[ ! -d "$source_checkout" ]]; then
    echo "Error: source checkout not found: $source_checkout" >&2
    exit 1
  fi
  source_checkout=$(cd "$source_checkout" && pwd -P)
  if [[ "$source_checkout" == "$repo_root" ]]; then
    echo "Error: --source must be a different checkout." >&2
    exit 1
  fi
fi

# ---- helpers ----------------------------------------------------------------

link_if_missing() {
  local src="$1"
  local dst="$2"
  local label="$3"

  if [[ -e "$dst" || -L "$dst" ]]; then
    return 0
  fi
  if [[ ! -e "$src" ]]; then
    return 0
  fi
  mkdir -p "$(dirname "$dst")"
  ln -s "$src" "$dst"
  printf '  linked  %s\n' "$label"
}

link_dir_entries() {
  local src_dir="$1"
  local dst_dir="$2"
  local label="$3"

  if [[ ! -d "$src_dir" ]]; then
    return 0
  fi
  mkdir -p "$dst_dir"
  local entry name dst
  for entry in "$src_dir"/*; do
    [[ -e "$entry" ]] || continue
    name=$(basename "$entry")
    dst="$dst_dir/$name"
    if [[ -e "$dst" || -L "$dst" ]]; then
      continue
    fi
    ln -s "$entry" "$dst"
    printf '  linked  %s/%s\n' "$label" "$name"
  done
}

run() {
  printf '  run     %s\n' "$*"
  "$@"
}

# ---- link gitignored inputs ------------------------------------------------

cd "$repo_root"

if [[ -n "$source_checkout" ]]; then
  echo "Linking gitignored inputs from $source_checkout:"
  link_dir_entries "$source_checkout/lib" "$repo_root/lib" "lib"
  link_dir_entries "$source_checkout/src/HotRepl.BepInEx/lib" \
                   "$repo_root/src/HotRepl.BepInEx/lib" \
                   "src/HotRepl.BepInEx/lib"
  link_if_missing "$source_checkout/Local.props" "$repo_root/Local.props" "Local.props"
else
  echo "No --source given; skipping Unity DLL linking (Core-only bootstrap)."
fi

# ---- restore per-worktree state --------------------------------------------

echo "Restoring per-worktree state:"
run "$repo_root/scripts/run-in-nix.sh" dotnet tool restore
run "$repo_root/scripts/run-in-nix.sh" bun install --frozen-lockfile
run "$repo_root/scripts/run-in-nix.sh" lefthook install

# ---- verify ----------------------------------------------------------------

echo "Verification:"
verify_path() {
  local path="$1"
  local label="$2"
  if [[ -e "$path" ]]; then
    printf '  ok      %s\n' "$label"
  else
    printf '  missing %s (%s)\n' "$label" "$path"
  fi
}

verify_path "$repo_root/.config/dotnet-tools.json" "dotnet tools manifest"
verify_path "$repo_root/lib/mcs.dll" "tracked Mono.CSharp runtime (lib/mcs.dll)"
if [[ -n "$source_checkout" ]]; then
  verify_path "$repo_root/lib/UnityEngine.CoreModule.dll" "Unity DLLs in lib/"
  verify_path "$repo_root/src/HotRepl.BepInEx/lib" "BepInEx host references"
fi
verify_path "$repo_root/node_modules" "Bun dependencies (node_modules)"

cat <<DONE

Worktree bootstrap complete.

Next steps:
  - Build Core:                    dotnet build src/HotRepl.Core/ --nologo -v q
  - Run unit tests:                dotnet test tests/HotRepl.Tests/ --nologo -v q
  - Build BepInEx host (if libs):  dotnet build src/HotRepl.BepInEx/ --nologo -v q
  - Run package tests:              bun test packages/*/test/**/*.test.ts
  - Run pre-push gate:             lefthook run pre-push --force
DONE
