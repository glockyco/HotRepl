## Why

HotRepl development and downstream loader builds currently depend on Homebrew, user-level tools,
sibling checkouts, and mutable per-worktree state. This makes clean worktrees, CI, and game projects
resolve different toolchains and leaves no revision-pinned command for producing a loader artifact.

## What Changes

- Add a flake that pins Bun, .NET SDKs, formatters, linters, hook tooling, and the repository
  development shell on Darwin and Linux.
- Add explicit bootstrap and diagnostic applications for cold worktrees without writing generated
  state into the Nix store.
- Add a revision-pinned loader build application that consumes caller-supplied game assemblies and
  writes artifacts plus provenance to a caller-owned output directory.
- Route local hooks, worktree bootstrap, and CI through the same Nix-owned commands.
- Remove Homebrew discovery, user-path fallbacks, and sibling-checkout defaults from canonical
  workflows.
- Keep CrossOver bottles, game installations, loader installations, secrets, and deployment state
  outside Nix.

## Capabilities

### New Capabilities

- `development-environment`: Pinned clean-shell, bootstrap, verification, and automatic-entry
  behavior for HotRepl contributors.
- `downstream-loader-builds`: Revision-pinned BepInEx and MelonLoader build inputs, outputs,
  provenance, and failure behavior for game repositories.

### Modified Capabilities

None.

## Impact

The change affects `flake.nix`, Nix package and command definitions, worktree bootstrap scripts,
Lefthook commands, GitHub Actions, repository guidance, and the downstream loader build entry point.
Existing C# protocol and runtime behavior do not change. Consumer repositories can migrate
independently by pinning the HotRepl flake and replacing sibling-checkout build commands with the
published application contract.
