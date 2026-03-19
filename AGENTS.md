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

## Architecture

- `src/HotRepl.Core/` — Framework-agnostic core (netstandard2.1)
- `src/HotRepl.BepInEx/` — BepInEx 5.x adapter
- `lib/Mono.CSharp.dll` — Built from mcs-unity (net35 target)

## Building

```bash
dotnet build                          # Build all projects
dotnet build src/HotRepl.BepInEx/     # Build BepInEx adapter only
```

## Non-Interactive Shell Commands

**ALWAYS use non-interactive flags** to avoid hanging on confirmation prompts:
```bash
cp -f source dest
mv -f source dest
rm -f file
rm -rf directory
```
