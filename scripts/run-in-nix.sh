#!/usr/bin/env bash
set -euo pipefail

if [[ -n "${IN_NIX_SHELL:-}" ]]; then
  exec "$@"
fi

command -v nix >/dev/null 2>&1 || {
  echo "Error: Nix is required. Enter the repository shell or install Nix." >&2
  exit 127
}

exec nix develop --command "$@"
