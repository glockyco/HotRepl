#!/usr/bin/env bash
set -euo pipefail

# IN_NIX_SHELL says a Nix shell is active, not that it is this repository's.
# Running a hook from another repository's shell sets it while providing none of
# the tools below, so require the command itself to resolve before trusting it.
if [[ -n "${IN_NIX_SHELL:-}" ]] && command -v "$1" >/dev/null 2>&1; then
  exec "$@"
fi

command -v nix >/dev/null 2>&1 || {
  echo "Error: Nix is required. Enter the repository shell or install Nix." >&2
  exit 127
}

exec nix develop --command "$@"
