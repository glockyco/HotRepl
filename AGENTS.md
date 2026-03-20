# Agent Instructions

HotRepl: runtime C# REPL over WebSocket for Mono-based Unity games. This file is for
agents working **on** this repo. For agents using HotRepl to inspect a running game,
see the skill at `.claude/skills/hotrepl/SKILL.md`.

## Issue Tracking

This repo uses **bd** (beads).

```bash
bd ready                     # list available work
bd show <id>                 # view issue details
bd update <id> --claim       # claim work atomically before starting
bd close <id>                # mark complete
```

Always claim before starting work. One issue at a time.

## Build & Test

### C# (always available, no game required)

```bash
dotnet build src/HotRepl.Core/ --nologo -v q           # core; CI gate
dotnet build src/HotRepl.BepInEx/ --nologo -v q        # adapter; requires Unity DLLs in lib/
dotnet test tests/HotRepl.Tests/ --nologo -v q         # unit tests
dotnet format src/HotRepl.Core/ --verify-no-changes    # format check; CI gate
dotnet format src/HotRepl.Core/                        # auto-fix formatting
```

CI path: build Core + format check. Run both before claiming a task complete.

### Python smoke tests (requires a running game with HotRepl loaded)

```bash
cd client
uv pip install -e '.[test]'
hotrepl ping                        # verify server is up
hotrepl test                        # full protocol smoke suite (~38 tests)
hotrepl test --url ws://host:port   # against a remote endpoint
```

Smoke tests skip automatically when no server is reachable. They exercise the full
protocol surface: eval, errors, state persistence, reset, ping, autocomplete,
subscriptions, and edge cases. Read `client/tests/` to understand protocol contracts.

## Project Structure

See the Architecture section of README.md for the full directory tree.

## Architecture Invariants

Do not break these without understanding all consequences:

- **Threading**: Fleck threads only enqueue to `ConcurrentQueue`s. The main thread
  (via `Tick()`) is the sole executor. Never call `_evaluator.Evaluate()` from a
  Fleck thread.
- **Tick drain order**: (1) cancel drain, (2) command queue, (3) at most one eval,
  (4) subscriptions. Do not reorder — cancel must precede eval dequeue so cancels
  issued this frame pre-empt queued jobs.
- **`IReplHost` is the sole platform boundary**: Core must never import BepInEx,
  UnityEngine, or any game-specific type. All game coupling flows through `IReplHost`.
- **C# 7.x in evaluated code**: Mono.CSharp evaluates C# 7. The project itself
  targets `netstandard2.1` and can use C# 8+ in host code, but evaluated user code
  is limited to C# 7. Do not attempt to raise this; it is a compiler version pin.
- **`mcs.dll`**: do not update without running the full smoke test suite. The
  compiler version determines what features users can evaluate.

## Domain Constraints Agents Often Get Wrong

- **`varName * expr` is a parser bug in mcs.dll**: when `varName` was defined in a
  prior eval, Mono's interactive parser reads `varName * 2` as a pointer-type
  declaration. Use `2 * varName`. This is a mcs.dll limitation — do not attempt to
  fix it in HotRepl.
- **`Thread.Abort` and tight loops**: Mono does not inject safepoints at loop
  back-edges. A `while(true){}` eval may not abort on timeout. This is a Mono
  runtime limitation — document it, do not work around it in HotRepl.
- **Type memory leak**: class/struct/enum definitions loaded via eval cannot be
  unloaded from the AppDomain. `reset` recreates the evaluator but does not free
  memory. This is inherent to Mono's JIT — document it, do not paper over it.
- **Single client**: only one WebSocket connection is active at a time. A new
  connection replaces the prior session and cancels all subscriptions.

## Adding Protocol Messages

1. Add a `MessageType` const in `Protocol/Messages.cs`.
2. Add the inbound and/or outbound record classes in `Protocol/Messages.cs`.
3. Handle the inbound type in `Server/MessageRouter.cs`.
4. Add an `IEngineCommand` implementation if the message needs main-thread dispatch.
5. Add the `eval_result` / `eval_error` / `*_result` response in `ReplEngine.cs`.
6. Add smoke test coverage in `client/tests/`.

## Code Conventions

- `netstandard2.1` for both `HotRepl.Core` and `HotRepl.BepInEx`.
- Newtonsoft.Json for protocol serialization. Do not add a second JSON library.
- Fleck for WebSocket. Do not add a second WebSocket library.
- XML doc comments on all public symbols in `IReplHost.cs`, `ReplEngine.cs`,
  `ReplConfig.cs`.
- `dotnet format` is the formatter; CI enforces it. Run before committing.

## Shell Conventions

Always use non-interactive flags to avoid hanging:

```bash
cp -f src dst
mv -f src dst
rm -f file
rm -rf directory
dotnet build --nologo -v q
```
