#!/usr/bin/env bash
set -euo pipefail

# Trust only this repository's shell. A foreign Nix shell can resolve a local
# script without providing the tools that the script invokes.
if [[ "${HOTREPL_DEV_SHELL:-}" == "1" ]]; then
  exec "$@"
fi

# GUI Git clients do not source the Nix profile, so their PATH can omit nix.
for profile in \
  /run/current-system/sw/bin \
  "/etc/profiles/per-user/${USER:-$(id -un)}/bin" \
  "$HOME/.nix-profile/bin" \
  /nix/var/nix/profiles/default/bin; do
  if [[ -d "$profile" && ":$PATH:" != *":$profile:"* ]]; then
    PATH="$PATH:$profile"
  fi
done
export PATH

command -v nix >/dev/null 2>&1 || {
  echo "Error: Nix is required. Enter the repository shell or install Nix." >&2
  exit 127
}

exec nix develop --command "$@"
