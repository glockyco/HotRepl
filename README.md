# HotRepl

[![@hotrepl/sdk](https://img.shields.io/npm/v/@hotrepl/sdk.svg?label=%40hotrepl%2Fsdk)](https://www.npmjs.com/package/@hotrepl/sdk)
[![@hotrepl/cli](https://img.shields.io/npm/v/@hotrepl/cli.svg?label=%40hotrepl%2Fcli)](https://www.npmjs.com/package/@hotrepl/cli)
[![@hotrepl/mcp](https://img.shields.io/npm/v/@hotrepl/mcp.svg?label=%40hotrepl%2Fmcp)](https://www.npmjs.com/package/@hotrepl/mcp)
[![@hotrepl/protocol](https://img.shields.io/npm/v/@hotrepl/protocol.svg?label=%40hotrepl%2Fprotocol)](https://www.npmjs.com/package/@hotrepl/protocol)
[![license](https://img.shields.io/github/license/glockyco/HotRepl.svg)](LICENSE)

HotRepl is a runtime C# REPL and typed command bridge for Unity games. It embeds in a game through
BepInEx/Mono or MelonLoader/IL2CPP, runs work on Unity's main thread, and exposes a local WebSocket
protocol for coding agents, CLIs, and MCP tools.

Use HotRepl when you need to inspect or automate a running Unity game without building a one-off
debug menu for every task. Raw eval is useful for exploration; typed commands are the stable
contract for repeatable exports, tests, and agent workflows.

## Requirements

- A Unity game running the BepInEx plugin (`HotRepl.BepInEx.dll`) or the MelonLoader mod
  (`HotRepl.Host.MelonLoader.dll`). The plugin opens `ws://127.0.0.1:18590` by default.
- Bun 1.3.14 or Node 20+ to consume the npm packages.

## Install

```bash
# Programmatic use from TypeScript:
bun add @hotrepl/sdk        # or npm install @hotrepl/sdk

# Command line:
bunx @hotrepl/cli info      # or npx -y @hotrepl/cli info
```

Wire the MCP server into your agent's config — any MCP-compatible host accepts this block:

```json
{
  "mcpServers": {
    "hotrepl": {
      "command": "npx",
      "args": ["-y", "@hotrepl/mcp"]
    }
  }
}
```

Each published package has its own README with details: [`@hotrepl/sdk`](packages/sdk/README.md) ·
[`@hotrepl/cli`](packages/cli/README.md) · [`@hotrepl/mcp`](packages/mcp/README.md) ·
[`@hotrepl/protocol`](packages/protocol/README.md).

## Quickstart

```ts
import { connect } from "@hotrepl/sdk";

const session = await connect(); // ws://127.0.0.1:18590 by default

// Raw eval — any C# expression, on the game's main thread:
const product = await session.eval<string>(
  "UnityEngine.Application.productName",
);
// → { hasValue: true, value: "Ardenfall", valueType: "System.String", durationMs: 7 }

// Typed, schema-validated game command:
const preflight = await session.run<{ writable: boolean; freeMb: number }>(
  "archive.preflight",
  {},
);
// → { output: { writable: true, freeMb: 41213 }, artifacts: {} }
```

Set `HOTREPL_URL` (or pass `url:` to `connect`) to point at a non-default host.

## Integration paths

| Path           | Use it for                                                   | Entry point                                          |
| -------------- | ------------------------------------------------------------ | ---------------------------------------------------- |
| Raw eval       | Interactive inspection and one-off repair snippets           | `Session.eval()` or `hotrepl eval`                   |
| Typed commands | Repeatable game automation, exports, and artifact collection | `Session.run()`, `commands_list`, `command_describe` |
| CLI            | Scripts and shell-driven local workflows                     | `@hotrepl/cli` (`hotrepl` binary)                    |
| MCP            | Agent tools with a small stable tool catalog                 | `@hotrepl/mcp` (`hotrepl-mcp` binary)                |
| Host embedding | New Unity loader adapters or test hosts                      | `IReplHost` + `ReplEngine.Tick()`                    |

## Protocol

The server sends a `handshake` frame immediately after the WebSocket connection opens. Every
request/response frame is UTF-8 JSON with a `type` discriminant and a caller-assigned `id` when a
response is expected.

Core request families:

```json
{ "type": "eval", "id": "e1", "code": "1 + 1" }
{ "type": "commands_list", "id": "c1" }
{ "type": "command_describe", "id": "c2", "name": "archive.preflight" }
{ "type": "command_call", "id": "c3", "name": "archive.preflight", "args": {} }
{ "type": "journal_query", "id": "j1", "limit": 10 }
```

Runtime errors use a universal `error` envelope with closed `kind` values. Job commands return
`job_accepted`; polling `job_status` eventually returns a terminal `job_result`. See
[`docs/control-plane-protocol.md`](docs/control-plane-protocol.md) for the full message inventory.

The default security model is loopback binding plus single-client replacement.

## Real consumers

- [Ardenfall Compendium](https://github.com/glockyco/ardenfall-compendium) uses the BepInEx/Mono
  path for a static compendium export pipeline. It is the reference consumer for game-specific typed
  commands, snapshot artifacts, and a Bun controller.
- [Ancient Kingdoms Compendium & Mods](https://github.com/glockyco/ancient-kingdoms-mods) uses the
  MelonLoader/IL2CPP path for data export and game automation. It is the reference consumer for
  build-tool-driven host deployment and export orchestration.

## Evaluation semantics

- Persistent evaluator state survives across evals until `reset`; generated types may remain loaded
  until process exit.
- Fleck callbacks enqueue work only. The main-thread `Tick()` path is the sole executor.
- Tick drain order is fixed: cancels, command queue, at most one eval, subscriptions.
- A new WebSocket connection replaces the previous client and emits `session_evicted`.
- Mono.CSharp evaluates C# 7.x; Roslyn evaluators report `latest`.
- Timeout enforcement is capability-driven: `hardAbort`, `cooperative`, or `unsupported`.

## Embedding

Implement [`IReplHost`](src/HotRepl.Core/IReplHost.cs) and drive `ReplEngine` from the host's main
thread:

```csharp
var engine = new ReplEngine(new MyHost());
engine.Start();

// per frame
engine.Tick();

engine.Dispose();
```

`IReplHost` is the only platform boundary. Core stays free of BepInEx, UnityEngine, MelonLoader,
Il2CppInterop, game-specific types, `mcs.dll`, and Roslyn packages.

## Building host adapters

```bash
dotnet build src/HotRepl.BepInEx/ --nologo -v q

dotnet build src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj \
  -p:MelonLoaderPath="/path/to/Game/MelonLoader" \
  -p:Il2CppAssembliesPath="/path/to/Game/MelonLoader/Il2CppAssemblies"
```

BepInEx deploys `HotRepl.BepInEx.dll`, `HotRepl.Core.dll`, `HotRepl.Protocol.dll`, Fleck,
Newtonsoft.Json, and `mcs.dll` side-by-side. MelonLoader deploys the host, Core, Protocol, Roslyn
evaluator, Unity helpers, Fleck, Newtonsoft.Json, and Roslyn dependencies in `Mods/`.

## Troubleshooting

| Symptom                                                             | Likely cause                                                                                                                                                                       |
| ------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `HotRepl WebSocket connection failed.` from CLI                     | Game isn't running, or the plugin/mod isn't loaded. Start the game and retry. The CLI exits 69 (`EX_UNAVAILABLE`).                                                                 |
| MCP host doesn't list HotRepl's tools                               | Most often a JSON syntax error in the host's MCP config — any error typically disables every server silently. Re-validate, then fully quit and re-launch the host application.     |
| Tool call returns `"HotRepl is not reachable…"`                     | Same root cause as the CLI symptom above: game/plugin not running. The MCP server itself is fine; the next tool call after the game starts will succeed.                           |
| `Session was evicted: displaced.`                                   | HotRepl is single-client. Another CLI or MCP session connected and replaced this one. Close the displacing client, or accept the eviction; the next call reconnects automatically. |
| Eval hangs                                                          | Mono.CSharp doesn't inject safepoints at loop back-edges, so a `while(true)` eval can't be aborted on timeout. Use `reset` (or restart the game) to recover.                       |
| `varName * expr` parses oddly after `varName` is defined in an eval | `mcs.dll` interactive-parser bug: `x * 2` is read as a pointer-type declaration when `x` is in scope. Use `2 * x` instead.                                                         |

## Development

For working on HotRepl itself, not just consuming it.

### Setup

```bash
brew install lefthook dprint actionlint commitlint typos
bun install --frozen-lockfile
dotnet tool restore
lefthook install
```

.NET 10.x is required because `HotRepl.Tests` targets `net10.0`.

### Verification

```bash
lefthook run pre-push --force
```

`pre-push` mirrors CI: Bun install/tests/typecheck/schema export, dprint, typos, actionlint, C#
build, and C# tests. See [`AGENTS.md`](AGENTS.md) for agent-specific constraints and targeted
commands.

### Running from source

Inside a clone, the SDK example above just works because Bun resolves `@hotrepl/sdk` to the
workspace copy. The CLI and MCP binaries don't exist on PATH until you publish (or `bun link`), so
invoke their source entry points directly:

```bash
# CLI:
bun packages/cli/src/index.ts info
bun packages/cli/src/index.ts eval 'UnityEngine.Application.productName'
bun packages/cli/src/index.ts run archive.preflight '{}'

# MCP — point your client at the source file by absolute path:
{
  "mcpServers": {
    "hotrepl": {
      "command": "bun",
      "args": ["/absolute/path/to/HotRepl/packages/mcp/src/index.ts"]
    }
  }
}
```

The workspace packages export their `src/` directly via the `bun` condition in each `package.json`,
so a Bun-driven build picks up source-level changes without needing a `bun run build`.

## Releases

The npm packages are released by [`changesets/action`](https://github.com/changesets/action) from
the `main` branch. Workflow file: [`.github/workflows/release.yml`](.github/workflows/release.yml).

If you ship code that affects a publishable package (`@hotrepl/protocol`, `@hotrepl/sdk`,
`@hotrepl/cli`, or `@hotrepl/mcp`), add a changeset to your PR:

```bash
bun changeset
```

Pick the packages you touched, pick a bump (`patch` / `minor` / `major`), and write a one-line
summary for consumers. The file lands under `.changeset/` and travels with the PR.

`updateInternalDependencies: "patch"` is enabled, so a `@hotrepl/protocol` minor bump auto-bumps
`@hotrepl/sdk` (and transitively `@hotrepl/cli` and `@hotrepl/mcp`) as patches — you only need to
list the package whose API actually changed.

On every push to `main`, the workflow:

1. Opens or updates a `chore(release): version packages` PR whenever pending changesets exist. That
   PR shows the proposed version bumps and the per-package `CHANGELOG.md` deltas.
2. When that PR is merged, the workflow builds every package's `dist/`, then publishes any
   `@hotrepl/*` whose `package.json` version isn't yet on npm. A `<package>@<version>` git tag is
   pushed and one GitHub Release per published package is created, with the CHANGELOG entry as its
   body.

`@hotrepl/testing` and `@hotrepl/conformance` are workspace-internal (`"private": true`) and never
publish. Publishing uses
[npm trusted publishing via OIDC](https://docs.npmjs.com/trusted-publishers/) — no long-lived
`NPM_TOKEN`. Provenance is generated automatically.

## License

MIT
