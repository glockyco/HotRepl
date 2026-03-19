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
    Evaluation/                        # MonoEvaluator, ICodeEvaluator, EvalResult
    Hosting/                           # IReplHost interface, ReplEngine, ReplConfig
    Protocol/                          # Message types, MessageSerializer
    Serialization/                     # ResultSerializer (value formatting)
    Server/                            # ReplServer (Fleck WebSocket)
    Polyfills/                         # IsExternalInit for record support
  HotRepl.BepInEx/                     # BepInEx 5.x adapter (netstandard2.1)
    ReplPlugin.cs                      # BepInEx plugin entry point
    BepInExHost.cs                     # IReplHost implementation
lib/
  Mono.CSharp.dll                      # Mono compiler (net35, from mcs-unity)
```

## Building

```bash
dotnet build                           # Build all projects
dotnet build src/HotRepl.Core/         # Build core only
dotnet build src/HotRepl.BepInEx/      # Build BepInEx adapter only
```

There are no tests yet. When added:
```bash
dotnet test                            # Run all tests
```

## Deploying for Testing

Copy the built DLLs to a BepInEx-enabled game:

```bash
GAME_DIR="/path/to/game"
cp -f src/HotRepl.Core/bin/Debug/netstandard2.1/HotRepl.Core.dll "$GAME_DIR/BepInEx/plugins/"
cp -f src/HotRepl.BepInEx/bin/Debug/netstandard2.1/HotRepl.BepInEx.dll "$GAME_DIR/BepInEx/plugins/"
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
