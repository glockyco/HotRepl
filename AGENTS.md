# Agent Instructions

HotRepl: runtime C# REPL and typed command bridge over WebSocket for Unity games through
BepInEx/Mono or MelonLoader/IL2CPP. This file is for agents working **on** this repo. For agents
using HotRepl to inspect a running game, see `.claude/skills/hotrepl/SKILL.md`.

## Commands

Bootstrap once per machine:

```bash
brew install lefthook dprint actionlint commitlint typos
bun install --frozen-lockfile
dotnet tool restore
lefthook install
```

`dotnet 10.x` is required because `HotRepl.Tests` targets `net10.0`. Bun is pinned by `package.json`
(`bun@1.3.14`).

Common local checks:

```bash
dotnet build src/HotRepl.Core/ --nologo -v q
dotnet test tests/HotRepl.Tests/ --nologo -v q
bun run test
bun run typecheck
bun run schemas:export
dprint check
typos
actionlint
```

Use lefthook for repo gates:

```bash
lefthook run pre-commit --all-files
lefthook run pre-push --force
```

`pre-commit` auto-fixes staged C# / TypeScript / docs formatting. `pre-push` mirrors the full CI
gate; CI also runs `lefthook run pre-push --force` in `hooks-parity`.

`--force` is required when no commits are ahead of `origin/HEAD` (e.g., a fresh checkout on `main`,
or manually validating before pushing). Without it, lefthook 2.x silently skips every command with
"no matching push files" and the gate passes vacuously. `git push` itself triggers the hook with
push refs and does not need `--force`.

## Targeted checks

### C# projects

```bash
dotnet build src/HotRepl.Core/ --nologo -v q
dotnet build src/HotRepl.BepInEx/ --nologo -v q     # requires Unity DLLs in lib/
dotnet build src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj \
  -p:MelonLoaderPath="/path/to/Game/MelonLoader" \
  -p:Il2CppAssembliesPath="/path/to/Game/MelonLoader/Il2CppAssemblies"
dotnet test tests/HotRepl.Tests/ --nologo -v q
```

`TreatWarningsAsErrors=true` is unconditional. CSharpier.MsBuild runs during every build, so
unformatted C# fails `dotnet build`.

### TypeScript packages

```bash
bun test packages/protocol/test
bun test packages/sdk/test packages/testing/test packages/conformance/test
bun test packages/cli/test
bun test packages/mcp/test
bun run --cwd packages/sdk typecheck
bun run --cwd packages/conformance typecheck
bun run --cwd packages/cli typecheck
bun run --cwd packages/mcp typecheck
```

Protocol/client behavior belongs in TypeScript package tests. Prefer `FakeRuntime` and `MockSession`
for deterministic SDK, CLI, MCP, and consumer-facade coverage.

## Verification expectations

- Run the narrowest command that covers the change before yielding.
- For C# behavior, prefer xUnit coverage under `tests/HotRepl.Tests/`.
- For protocol, SDK, CLI, MCP, and conformance behavior, update package tests under
  `packages/*/test`.
- Before claiming branch-level completion, run `lefthook run pre-push --force`.

## Architecture Invariants

These are non-discoverable requirements; do not "simplify" them away:

- Fleck/WebSocket callbacks enqueue only. The main thread `Tick()` is the sole executor.
- Tick drain order is fixed: cancels, command queue, at most one eval, subscriptions.
- `IReplHost` is the only platform boundary. Core must not import BepInEx, UnityEngine, MelonLoader,
  Il2CppInterop, game-specific types, `mcs.dll`, or Roslyn packages.
- Control handlers execute through the main-thread tick path, never directly from WebSocket
  callbacks.
- v2 has no auth/lease protocol. Loopback plus single-client replacement is the authority boundary.
- Addressed control responses go only to the originating connection; never fall back to a
  replacement client.
- Artifacts are references (`uri`, `path`, `sha256`, `byteSize`, `finalized`), not bulk payloads.
- BepInEx ships `HotRepl.Core.dll` and Core dependencies side-by-side; do not ILRepack/internalize
  Core, Fleck, or Newtonsoft.Json into `HotRepl.BepInEx.dll`.
- Evaluator timeout is capability-driven. `HardAbort` may abort the main thread; `Cooperative`
  cannot preempt every runtime loop.
- Mono.CSharp evaluates user code as C# 7.x. Do not raise this without replacing the evaluator.
- Do not update `mcs.dll` without running the full smoke test suite against a running game.

## Domain Constraints Agents Often Get Wrong

- **`varName * expr` is a parser bug in mcs.dll**: when `varName` was defined in a prior eval,
  Mono's interactive parser reads `varName * 2` as a pointer-type declaration. Use `2 * varName`.
  This is a mcs.dll limitation — do not attempt to fix it in HotRepl.
- **`Thread.Abort` and tight loops**: Mono does not inject safepoints at loop back-edges. A
  `while(true){}` eval may not abort on timeout. This is a Mono runtime limitation — document it, do
  not work around it in HotRepl.
- **Type memory leak**: class/struct/enum definitions loaded via persistent eval sessions may not be
  reclaimed until process exit. `reset` recreates the evaluator state but is not a full assembly
  unload boundary; use `Roslyn.Isolated` for stateless .NET 6 audit snippets.
- **Single client**: only one WebSocket connection is active at a time. A new connection replaces
  the prior session and cancels all subscriptions.

## Adding Protocol Messages

1. Add a `MessageType` const in `src/HotRepl.Protocol/MessageType.cs`.
2. Add the inbound or outbound C# record in `src/HotRepl.Protocol/Messages/Inbound/` or
   `Messages/Outbound/` (one public type per file; CI's MA0048 enforces this).
3. Add or update TypeScript types and schemas in `packages/protocol/src/`.
4. Handle the inbound type in `src/HotRepl.Core/Server/MessageRouter.cs`.
5. Add an `IEngineCommand` implementation if the message needs main-thread dispatch.
6. Add the `eval_result` / `eval_error` / `*_result` response in `ReplEngine.cs`.
7. Add xUnit and/or package test coverage.

## Code Conventions

- `netstandard2.1` for both `HotRepl.Core` and `HotRepl.BepInEx`.
- Newtonsoft.Json for C# protocol serialization. Do not add a second C# JSON library.
- Fleck for WebSocket. Do not add a second WebSocket library.
- Bun workspaces own the SDK, CLI, MCP, testing, and conformance packages.
- XML doc comments on all public symbols in `IReplHost.cs`, `ReplEngine.cs`, `ReplConfig.cs`.
- CSharpier formats every `.cs` file. CSharpier.MsBuild fails the build on unformatted code; the
  `pre-commit` hook auto-fixes staged C# before each commit.
- No broad suppression baselines. Use targeted `[SuppressMessage]` with justification when a
  suppression is semantically required.

## Commit Guidelines

See `.claude/skills/commit-guidelines/SKILL.md` for the full conventions. Short version:
`type(scope): imperative summary` — prose body explaining why, not what. One concept per commit. No
attribution lines. The `commit-msg` lefthook hook runs `commitlint` against `commitlint.config.js`,
so a non-conformant message is rejected before the commit lands.

## Releases

The four publishable npm packages (`@hotrepl/protocol`, `@hotrepl/sdk`, `@hotrepl/cli`,
`@hotrepl/mcp`) are released by `.github/workflows/release.yml` driven by
[changesets](https://github.com/changesets/changesets). `@hotrepl/testing` and
`@hotrepl/conformance` are workspace-internal (`"private": true`) and never publish.

When you ship a PR that changes any publishable package, add a changeset:

```bash
bun changeset
```

Pick the affected package(s) and a bump type (`patch` / `minor` / `major`). Write the summary for
the consumer, not the diff — it becomes the `CHANGELOG.md` entry and the GitHub Release body.

`updateInternalDependencies: "patch"` is configured, so internal dependents auto-bump as patches.
Listing only the directly-changed package is enough.

The workflow opens a `chore(release): version packages` PR on push to `main`. Merging that PR
publishes via npm trusted publishing (OIDC, no token) and creates one tagged GitHub Release per
published package.

## Worktrees

Project-local worktrees go in `.worktrees/<branch-name>` (gitignored). Each worktree needs
`dotnet tool restore`, `bun install --frozen-lockfile`, and (for the BepInEx host) gitignored Unity
DLLs linked from another checkout. The canonical bootstrap is:

```bash
scripts/bootstrap-worktree.sh [--source <trusted-checkout>]
```

Use a worktree whenever a change touches multiple commits; do not work on `main` directly for
non-trivial work.

## Shell Conventions

Always use non-interactive flags to avoid hanging:

```bash
cp -f src dst
mv -f src dst
rm -f file
rm -rf directory
dotnet build --nologo -v q
```
