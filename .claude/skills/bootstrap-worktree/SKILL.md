---
name: bootstrap-worktree
description: Use when starting work in a fresh git worktree, or when build/test failures point at missing Unity DLLs, missing dotnet tools, or a missing Python venv in this checkout.
---

# Bootstrap Worktree

Fresh git worktrees contain tracked files only. HotRepl also needs:

- gitignored Unity DLLs in `lib/` and `src/HotRepl.BepInEx/lib/` (for the BepInEx host build)
- optional `Local.props`
- restored `.dotnet/tools/` (csharpier)
- `client/.venv/` from `uv sync` (until the Python client is removed)

before builds and tests are meaningful.

## Canonical command

Run from the worktree root:

```bash
scripts/bootstrap-worktree.sh [--source <trusted-checkout>] [--no-python]
```

`--source` is required only if you intend to build the BepInEx host in this worktree. For Core-only
work (the common case during the v2 rewrite), omit it.

The script:

- never overwrites tracked files
- only links missing gitignored inputs from `<trusted-checkout>`
- always runs `dotnet tool restore` and (unless `--no-python`) `uv sync` inside `client/`
- prints a final verification report listing what is present and what is still missing

## Failure classification

Before bootstrap succeeds, these are setup failures, not code failures:

- `csharpier: command not found`
- `dotnet build src/HotRepl.BepInEx/` fails with missing `UnityEngine.*` references
- `uv: command not found` (install `uv` first)
- `pytest`/`uv run` errors caused by an absent `client/.venv/`

After bootstrap succeeds, remaining failures are code or data issues and should be debugged
normally.

## Worktree layout convention

- Project-local worktrees live under `.worktrees/<branch-name>` (gitignored).
- One branch per worktree; do not check out the same branch in two worktrees.
- Each worktree maintains its own `bin/`, `obj/`, `.dotnet/tools/`, `client/.venv/`.

If you have a "primary" checkout that already contains Unity DLLs, point `--source` at it to share
those large binaries via symlinks rather than copying.

## When NOT to use this skill

- Pure documentation edits in the main checkout. The bootstrap script is for worktrees that need
  build/test capability.
- Inside a non-HotRepl repository. The script refuses to run if
  `src/HotRepl.Core/HotRepl.Core.csproj` is absent.
