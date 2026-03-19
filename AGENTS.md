# Agent Instructions

This project uses **bd** (beads) for issue tracking. Run `bd onboard` to get started.

## Project Overview

HotRepl is a standalone, agent-first runtime C# REPL for game modding. It runs inside
any Mono-based game (Unity, MonoGame, etc.) and accepts C# code over WebSocket for
compilation and execution. Built for LLM coding agents but usable by humans too.

## Quick Reference

```bash
bd ready              # Find available work
bd show <id>          # View issue details
bd update <id> --claim  # Claim work atomically
bd close <id>         # Complete work
```

## Project Structure

```
HotRepl.slnx                          # Solution file (slnx format)
src/
  HotRepl.Core/                        # Framework-agnostic core (netstandard2.1)
    Evaluation/                        # MonoEvaluator, ICodeEvaluator, EvalResult, AssemblyFilter
    Hosting/                           # IReplHost interface, ReplEngine, ReplConfig
    Protocol/                          # Message types, MessageSerializer
    Serialization/                     # ResultSerializer (value formatting)
    Helpers/                           # ReplHelpers (injected REPL helper methods)
    Server/                            # ReplServer (Fleck WebSocket)
    Polyfills/                         # IsExternalInit for record support
  HotRepl.BepInEx/                     # BepInEx 5.x adapter (netstandard2.1)
    ReplPlugin.cs                      # BepInEx plugin entry point
    BepInExHost.cs                     # IReplHost implementation
tests/
  HotRepl.Tests/                       # C# unit tests (xUnit, net10.0)
client/                                # Python reference client + smoke tests
  src/hotrepl/                         # Client library + CLI
  tests/                               # Protocol smoke test suite (~38 tests)
lib/
  mcs.dll                              # Mono compiler (mcs-unity)
```

## Building

```bash
dotnet build src/HotRepl.Core/         # Build core (CI default)
dotnet build src/HotRepl.BepInEx/      # Build adapter (requires Unity DLLs in lib/)
```

## Testing

### C# unit tests (always available, no game required)
```bash
dotnet test tests/HotRepl.Tests/       # Run C# unit tests
```
Tests cover serialization round-trips, result formatting, protocol types, and
configuration defaults. These run in CI on every push.

### Python client + smoke tests (requires a running game)
```bash
cd client && uv pip install -e '.[test]'  # Install client + test deps
hotrepl ping                              # Check if a game is running with HotRepl
hotrepl eval '1 + 1'                      # Evaluate C# code
hotrepl test                              # Run protocol smoke tests
hotrepl test --url ws://host:port         # Against a remote game
```
The smoke tests exercise the full WebSocket protocol: eval, errors, state
persistence, reset, ping, autocomplete, subscriptions, and edge cases. They
skip automatically when no server is reachable.

### What you can't test without a game
- Code evaluation against game assemblies (UnityEngine, game types)
- Plugin loading via BepInEx
- Unity main-thread execution (frame-driven Tick())
- Thread.Abort timeout behavior (Mono-only)
- Helper methods that use UnityEngine (Screenshot, SceneGraph)

The smoke tests in `client/tests/` are the protocol contract. Read them to
understand exactly what HotRepl promises to its clients.


## Code Quality

CI enforces build + format checks on HotRepl.Core. A pre-commit hook mirrors these locally:

```bash
git config core.hooksPath .githooks    # One-time setup (activates pre-commit hook)
dotnet format src/HotRepl.Core/        # Auto-fix formatting before commit
```

The hook runs `dotnet build` and `dotnet format --verify-no-changes` automatically on each commit.

## Deploying for Testing

Copy the built DLLs to a BepInEx-enabled game:

```bash
GAME_DIR="/path/to/game"
cp -f src/HotRepl.BepInEx/bin/Debug/netstandard2.1/HotRepl.BepInEx.dll "$GAME_DIR/BepInEx/plugins/"
cp -f src/HotRepl.BepInEx/bin/Debug/netstandard2.1/mcs.dll "$GAME_DIR/BepInEx/plugins/"
```

Launch the game, then connect to `ws://localhost:18590`.

## Beads Usage

This project uses [beads](https://github.com/cosmicpudding/beads) (bd) for issue tracking.

```bash
bd onboard            # First-time setup
bd ready              # List available work
bd show <id>          # View issue details
bd update <id> --claim  # Claim an issue
bd close <id>         # Mark complete
```

Always claim an issue before starting work. Close it when done.

## Non-Interactive Shell Commands

**ALWAYS use non-interactive flags** to avoid hanging on confirmation prompts:
```bash
cp -f source dest
mv -f source dest
rm -f file
rm -rf directory
dotnet build --nologo -v q    # Quiet build output
```
