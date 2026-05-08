# Agent Instructions

HotRepl: runtime C# REPL over WebSocket for Unity games through BepInEx/Mono or MelonLoader/IL2CPP.
This file is for agents working **on** this repo. For agents using HotRepl to inspect a running
game, see the skill at `.claude/skills/hotrepl/SKILL.md`.

## Build & Test

All tooling is local-first and version-pinned. The lefthook gate catches at least everything CI
catches; CI explicitly runs `lefthook run pre-push` in a `hooks-parity` job to keep the invariant.

### Bootstrap (one-time, per machine)

```bash
brew install lefthook dprint actionlint commitlint typos
dotnet tool restore
lefthook install
```

`dotnet 10.x` is required: `HotRepl.Tests` targets `net10.0`. The brew formula for `commitlint`
resolves the canonical `@commitlint/cli`. Versions are pinned in `.config/dotnet-tools.json`,
`commitlint.config.js`, `dprint.json`, and `lefthook.yml`.

### C# (always available, no game required)

```bash
dotnet build src/HotRepl.Core/ --nologo -v q       # core; CI gate
dotnet build src/HotRepl.BepInEx/ --nologo -v q    # adapter; requires Unity DLLs in lib/
dotnet test  tests/HotRepl.Tests/ --nologo -v q    # unit tests
dotnet csharpier check src/ tests/                 # standalone format check
dotnet csharpier format src/ tests/                # auto-fix formatting
```

`TreatWarningsAsErrors=true` is unconditional via `Directory.Build.props`. CSharpier.MsBuild runs
during every build, so an unformatted `.cs` file fails `dotnet build` directly — the standalone
`csharpier check` is only for editor scripting and CI fast-feedback.

### Python (no game required)

```bash
uvx ruff check                                          # lint
uvx ruff format --check                                 # format check
uvx pyright                                             # strict type check
uv run --project client --extra test pytest client/tests/ -v
```

Pyright runs in strict mode against `client/src`, `client/tests`, and `scripts`. Ruff config covers
the same tree from the repo-root `pyproject.toml`.

### Cross-language

```bash
dprint check        # md / json / yaml / toml
typos               # spell check (excludes lib/, lockfiles, caches)
actionlint          # GitHub Actions workflows
lefthook run pre-commit --all-files   # mirror everything pre-commit checks
lefthook run pre-push                 # mirror the full CI gate locally
```

### MelonLoader host

`src/HotRepl.Host.MelonLoader` is not built in CI because it requires game-local MelonLoader and
IL2CPP assemblies. On this workstation, pass `MelonLoaderPath` and `Il2CppAssembliesPath` from the
target game's install. Do not hard-code Ancient Kingdoms paths in HotRepl source files. The strict
analyzer / TreatWarningsAsErrors policy applies to these projects automatically when someone with a
game install builds them.

### Python smoke tests against a running game

```bash
cd client
uv pip install -e '.[test]'
hotrepl ping                        # verify server is up
hotrepl test                        # full protocol smoke suite
hotrepl test --url ws://host:port   # against a remote endpoint
```

Smoke tests skip automatically when no server is reachable. They exercise the full protocol surface:
eval, errors, state persistence, reset, ping, autocomplete, subscriptions, and edge cases. Read
`client/tests/` to understand protocol contracts.

## Toolchain

Established, single-purpose, version-pinned. Each tool either replaces multiple older tools, catches
a class of bugs none of the others cover, or is load-bearing for the local≥CI invariant. See
`docs/plans/lint-format-overhaul.md` for the full decision log.

| Concern             | Tool                                                       | Pinned in                          |
| ------------------- | ---------------------------------------------------------- | ---------------------------------- |
| C# format           | CSharpier (dotnet local tool + MSBuild check)              | `.config/dotnet-tools.json`        |
| C# analyzers        | NetAnalyzers + Meziantou.Analyzer + xunit.analyzers (test) | `Directory.Packages.props`         |
| Package versions    | Central Package Management (`Directory.Packages.props`)    | `Directory.Packages.props`         |
| Python lint+format  | Ruff                                                       | repo-root `pyproject.toml`         |
| Python type check   | pyright (strict)                                           | repo-root `pyproject.toml`         |
| md/json/yaml/toml   | dprint with pinned WASM plugins                            | `dprint.json`                      |
| Spell check         | typos (crate-ci)                                           | `_typos.toml`                      |
| Actions YAML        | actionlint                                                 | `.github/workflows/ci.yml` install |
| Hooks orchestrator  | lefthook                                                   | `lefthook.yml`                     |
| Commit messages     | `@commitlint/cli` with inline Conventional Commits ruleset | `commitlint.config.js`             |
| Whitespace baseline | `.editorconfig` (slimmed; CSharpier owns C# layout)        | `.editorconfig`                    |

Policies:

- `TreatWarningsAsErrors=true` is unconditional — Debug, Release, IDE, CI.
- No suppression baseline. Targeted, justified `[SuppressMessage]` per call site only when
  semantically required; broad `dotnet_diagnostic.*` overrides are limited to the test project's
  `tests/.editorconfig` with documented reasons.
- The lefthook gate (pre-commit + pre-push) catches everything CI catches. The `hooks-parity` CI job
  runs `lefthook run pre-push` directly so drift is impossible.
- Conventional Commits is enforced by the `commit-msg` hook locally and the `commit-lint` PR job in
  CI. The type enum is `build, chore, ci, docs, feat, fix, perf, refactor, revert, style, test`.

## Project Structure

See the Architecture section of README.md for the full directory tree.

## Architecture Invariants

Do not break these without understanding all consequences:

- **Threading**: Fleck threads only enqueue to `ConcurrentQueue`s. The main thread (via `Tick()`) is
  the sole executor. Never call `_evaluator.Evaluate()` from a Fleck thread.
- **Tick drain order**: (1) cancel drain, (2) command queue, (3) at most one eval, (4)
  subscriptions. Do not reorder — cancel must precede eval dequeue so cancels issued this frame
  pre-empt queued jobs.
- **`IReplHost` is the sole platform boundary**: Core must never import BepInEx, UnityEngine, or any
  game-specific type. All game coupling flows through `IReplHost`.
- **Control plane remains game-agnostic**: core may define typed command, job, lease, and artifact
  envelopes, but game-specific export behavior belongs in host command handlers.
- **Global control registry stays game-agnostic**: the registry stores `IControlCommandHandler`
  instances only. Do not add game-specific types, command names, or export policy to HotRepl.
- **BepInEx ships `HotRepl.Core.dll` and Core dependencies side-by-side**: do not
  ILRepack/internalize Core, Fleck, or Newtonsoft.Json into `HotRepl.BepInEx.dll`. Separate game
  plugins compiled against Core must share the exact same assembly identity for
  `GlobalControlCommandRegistry.Instance`, and Core must be able to load its runtime dependencies.
- **Control handlers execute through the main-thread tick path**: Fleck threads enqueue only. Do not
  execute control handlers directly from WebSocket callbacks.
- **Mutating control commands require a lease**: descriptors with `MutatesState` must reject calls
  without a valid exclusive control lease.
- **Control response ownership is strict**: addressed control responses must be delivered only to
  the originating connection and must not fall back to a replacement client.
- **Artifacts are references, not bulk payloads**: return metadata (`uri`, `path`, `sha256`,
  `byteSize`, `finalized`) and let external orchestrators verify files.
- **Evaluator timeout is capability-driven**: `TimeoutMode.HardAbort` may abort the main thread;
  `TimeoutMode.Cooperative` cancels a token and cannot preempt every runtime loop. Do not claim all
  evaluators have hard timeouts.
- **Core has no compiler stack**: Mono.CSharp and Roslyn live in evaluator projects. Core must not
  reference `mcs.dll`, Roslyn packages, UnityEngine, BepInEx, MelonLoader, or Il2CppInterop.
- **C# 7.x in Mono.CSharp evaluated code**: Mono.CSharp evaluates C# 7. Host projects can target
  newer frameworks/languages, but evaluated user code is limited to C# 7 under Mono.CSharp. Do not
  attempt to raise this without replacing the evaluator; it is a compiler version pin.
- **`mcs.dll`**: do not update without running the full smoke test suite. The compiler version
  determines what features Mono.CSharp users can evaluate.

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

## Commit Guidelines

See `.claude/skills/commit-guidelines/SKILL.md` for the full conventions. Short version:
`type(scope): imperative summary` — prose body explaining why, not what. One concept per commit. No
attribution lines. The `commit-msg` lefthook hook runs `commitlint` against `commitlint.config.js`,
so a non-conformant message is rejected before the commit lands.

## Shell Conventions

Always use non-interactive flags to avoid hanging:

```bash
cp -f src dst
mv -f src dst
rm -f file
rm -rf directory
dotnet build --nologo -v q
```
