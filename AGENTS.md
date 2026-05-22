# Agent Instructions

HotRepl: runtime C# REPL over WebSocket for Unity games through BepInEx/Mono or MelonLoader/IL2CPP.
This file is for agents working **on** this repo. For agents using HotRepl to inspect a running
game, see the skill at `.claude/skills/hotrepl/SKILL.md`.

## Commands

Bootstrap once per machine:

```bash
brew install lefthook dprint actionlint commitlint typos
dotnet tool restore
lefthook install
```

`dotnet 10.x` is required because `HotRepl.Tests` targets `net10.0`.

Common local checks:

```bash
dotnet build src/HotRepl.Core/ --nologo -v q
dotnet test tests/HotRepl.Tests/ --nologo -v q
uvx ruff check
uvx ruff format --check
uv run --project client --extra dev pyright
dprint check
typos
actionlint
```

Use lefthook for repo gates:

```bash
lefthook run pre-commit --all-files
lefthook run pre-push --force
```

`pre-commit` auto-fixes staged C# / Python / docs formatting. `pre-push` mirrors the full CI gate;
CI also runs `lefthook run pre-push --force` in `hooks-parity`.

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

### Python smoke tests against a running game

```bash
cd client
uv pip install -e '.[test]'
hotrepl ping
hotrepl test
hotrepl test --url ws://host:port
```

Smoke tests skip automatically when no server is reachable. They exercise eval, errors, state
persistence, reset, ping, autocomplete, subscriptions, and edge cases.

## Verification expectations

- Run the narrowest command that covers the change before yielding.
- For protocol/client behavior, update or add coverage in `client/tests/`.
- For C# behavior, prefer xUnit coverage under `tests/HotRepl.Tests/`.
- Before claiming branch-level completion, run `lefthook run pre-push`.

## Architecture Invariants

These are non-discoverable requirements; do not "simplify" them away:

- Fleck/WebSocket callbacks enqueue only. The main thread `Tick()` is the sole executor.
- Tick drain order is fixed: cancels, command queue, at most one eval, subscriptions.
- `IReplHost` is the only platform boundary. Core must not import BepInEx, UnityEngine, MelonLoader,
  Il2CppInterop, game-specific types, `mcs.dll`, or Roslyn packages.
- Control handlers execute through the main-thread tick path, never directly from WebSocket
  callbacks.
- Mutating control commands require a valid exclusive lease.
- Addressed control responses go only to the originating connection; never fall back to a
  replacement client.
- Artifacts are references (`uri`, `path`, `sha256`, `byteSize`, `finalized`), not bulk payloads.
- BepInEx ships `HotRepl.Core.dll` and Core dependencies side-by-side; do not ILRepack/internalize
  Core, Fleck, or Newtonsoft.Json into `HotRepl.BepInEx.dll`.
- Evaluator timeout is capability-driven. `HardAbort` may abort the main thread; `Cooperative`
  cannot preempt every runtime loop.
- Mono.CSharp evaluates user code as C# 7.x. Do not raise this without replacing the evaluator.
- Do not update `mcs.dll` without running the full smoke test suite.

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

1. Add a `MessageType` const in `Protocol/MessageType.cs`.
2. Add the inbound or outbound record class as its own file in `Protocol/Inbound/` or
   `Protocol/Outbound/` (one type per file; CI's MA0048 enforces this).
3. Handle the inbound type in `Server/MessageRouter.cs`.
4. Add an `IEngineCommand` implementation if the message needs main-thread dispatch.
5. Add the `eval_result` / `eval_error` / `*_result` response in `ReplEngine.cs`.
6. Add smoke test coverage in `client/tests/`.

## Code Conventions

- `netstandard2.1` for both `HotRepl.Core` and `HotRepl.BepInEx`.
- Newtonsoft.Json for protocol serialization. Do not add a second JSON library.
- Fleck for WebSocket. Do not add a second WebSocket library.
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

## Worktrees

Project-local worktrees go in `.worktrees/<branch-name>` (gitignored). Each worktree needs
`dotnet tool restore`, optionally `cd client && uv sync`, and (for the BepInEx host) gitignored
Unity DLLs linked from another checkout. The canonical bootstrap is:

```bash
scripts/bootstrap-worktree.sh [--source <trusted-checkout>] [--no-python]
```

See `.claude/skills/bootstrap-worktree/SKILL.md` for details and failure modes. Use a worktree
whenever a change touches multiple commits; do not work on `main` directly for non-trivial work.

## Shell Conventions

Always use non-interactive flags to avoid hanging:

```bash
cp -f src dst
mv -f src dst
rm -f file
rm -rf directory
dotnet build --nologo -v q
```
