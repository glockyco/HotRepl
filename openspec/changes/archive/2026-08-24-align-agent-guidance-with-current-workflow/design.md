## Context

See `proposal.md` for motivation. Three mechanisms currently conflict:

- `AGENTS.md`, `.#bootstrap`, and `bootstrap-worktree` make additional worktrees the normal setup
  path.
- The local `commit-guidelines` skill duplicates global `commit-policy` behavior and permits
  bodyless commits that global policy rejects.
- The HotRepl usage skill demonstrates `archive.preflight`, while `UnityCommandCatalogNames`
  registers only `unity.app.info`, `unity.gameobject.find`, `unity.time.set_scale`, and
  `unity.screenshot.capture`.

The bootstrap script also performs useful dependency restoration. Removing its worktree model must
not remove the primary checkout's toolchain path.

## Goals / Non-Goals

**Goals:**

- Give repository development one documented checkout model.
- Keep command examples tied to executable catalog data.
- Give generic commit policy one owner.
- Preserve clear diagnostics for optional Unity assembly inputs.

**Non-Goals:**

- Change the HotRepl protocol or command registry.
- Vendor Unity assemblies or commit machine-local paths.
- Remove Git's ordinary working-tree terminology from Git errors or APIs.
- Prohibit a temporary worktree inside an isolated test if one is later needed.

## Decisions

### Remove the worktree surface as one clean cutover

Delete the skill, script, flake application, `.worktrees/` ignore entry, and live instructions
together. A partial removal leaves another discovery route that can still bias an agent.

Dependency restoration uses the pinned shell directly:

- `dotnet tool restore` restores repository-managed .NET tools.
- `bun install --frozen-lockfile` restores JavaScript dependencies.
- `lefthook install` installs repository hooks.

`AGENTS.md` states these primary-checkout steps only where the shell does not already perform them.
The existing doctor application gains checks for required tools and optional host assemblies instead
of acquiring files from another checkout.

Alternative: rename the script to `bootstrap-checkout`. Rejected. Its distinctive behavior is
linking ignored files from another checkout, which preserves the retired topology under a new name.

### Generate the example allow-list from the first-party catalog

A narrow repository check extracts command names from operational skill examples and compares them
with the catalog metadata. The check reads the same stable names that tests already use. It does not
maintain a second handwritten list.

Replace `archive.preflight` with `unity.app.info`, a registered command that accepts empty arguments
and is safe to demonstrate. Keep protocol-shape examples separate from game-specific catalog
examples.

Alternative: remove all command examples. Rejected. A correct example remains useful for showing
`command_describe`, `command_call`, and CLI syntax.

### Delete generic commit prose

Delete `commit-guidelines` and replace the `AGENTS.md` section with the global route plus only
enforced local facts. `commitlint.config.js` remains the authority for types, scopes, and subject
constraints.

Alternative: shorten the local skill. Rejected. Its remaining generic guidance would still duplicate
global policy and could drift independently.

### Keep optional host inputs explicit

The BepInEx host build still depends on ignored Unity assemblies. The doctor reports whether those
inputs exist and points to the project that owns them. It does not copy, link, or search another
checkout.

Core, protocol, SDK, CLI, MCP, and test work remain valid without optional host assemblies.

## Risks / Trade-offs

- Removing `.#bootstrap` can break a remembered command. The replacement is ordinary pinned-shell
  setup, documented before removal.
- A source parser for command examples can reject illustrative external commands. Limit enforcement
  to examples marked as first-party HotRepl commands.
- Doctor diagnostics can become another setup procedure. Keep it observational; it must not mutate
  dependencies or local assemblies.
