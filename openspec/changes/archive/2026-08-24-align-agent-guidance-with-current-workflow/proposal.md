## Why

HotRepl agent guidance recommends a worktree workflow that is no longer used. It also duplicates
global commit policy and demonstrates `archive.preflight`, a command that no first-party catalog
registers.

These instructions can steer an agent toward unnecessary checkouts or commands that fail at runtime.
The repository needs one accurate guidance surface tied to executable checks.

## What Changes

- **BREAKING** Remove the `bootstrap-worktree` skill, `scripts/bootstrap-worktree.sh`, the
  `.#bootstrap` flake application, `.worktrees/` convention, and live worktree instructions.
- Make the primary checkout the only documented development location.
- Keep dependency restoration in the existing pinned development shell and report optional Unity
  assembly requirements through the existing doctor path.
- Remove the local `commit-guidelines` skill and route commit behavior through the global
  `commit-policy` skill without copying its rules.
- Replace `archive.preflight` examples with a registered first-party Unity command.
- Add an automated check that rejects unknown first-party command names in operational skill
  examples.
- Keep repository-specific commit types and scopes only where `commitlint.config.js` enforces them.

## Capabilities

### New Capabilities

- `agent-guidance`: Defines how HotRepl guidance remains discoverable, current, nonduplicative, and
  consistent with registered commands and supported checkout behavior.

### Modified Capabilities

None. No accepted specification currently covers repository agent guidance.

## Impact

- **Deleted:** `.claude/skills/bootstrap-worktree/`, `.claude/skills/commit-guidelines/`, and
  `scripts/bootstrap-worktree.sh`.
- **Changed:** `AGENTS.md`, `.gitignore`, `flake.nix`, the HotRepl usage skill, repository checks,
  and related tests.
- **Development:** contributors restore dependencies in the primary checkout through the pinned
  shell. BepInEx host builds still require the documented local Unity assemblies.
- **Runtime:** no protocol, evaluator, SDK, host, or command behavior changes.
