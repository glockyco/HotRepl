#!/bin/sh
set -eu

for dir in "$HOME/.bun/bin" /opt/homebrew/bin /usr/local/bin; do
  if [ -x "$dir/bun" ]; then
    PATH="$dir:$PATH"
    export PATH
    exec "$dir/bun" "$@"
  fi
done

exec bun "$@"
