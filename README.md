# HotRepl

HotRepl is a runtime C# REPL and typed command bridge for Unity games. It embeds in a game through
BepInEx/Mono or MelonLoader/IL2CPP, runs work on Unity's main thread, and exposes a local WebSocket
protocol for coding agents, CLIs, and MCP tools.

Use HotRepl when you need to inspect or automate a running Unity game without building a one-off
debug menu for every task. Raw eval is useful for exploration; typed commands are the stable
contract for repeatable exports, tests, and agent workflows.

## Requirements

- .NET 10.x for the Core test project.
- Bun 1.3.14 for the TypeScript protocol, SDK, CLI, MCP, testing, and conformance packages.
- Unity/BepInEx or MelonLoader assemblies only when building host adapters.

## Quickstart

```bash
bun install --frozen-lockfile
dotnet tool restore

dotnet build src/HotRepl.Core/ --nologo -v q
dotnet test tests/HotRepl.Tests/ --nologo -v q
bun run test
bun run typecheck
```

A running host listens on `ws://127.0.0.1:18590` by default. The SDK and CLI use that URL unless
`HOTREPL_URL` or an explicit URL is supplied.

```bash
# Inspect the runtime handshake.
bun packages/cli/src/index.ts info --json

# Evaluate C# on the game's main thread.
bun packages/cli/src/index.ts eval 'UnityEngine.Application.productName'

# Run a typed game command exposed by the host.
bun packages/cli/src/index.ts run archive.preflight '{}'

# Inspect recent eval/command results.
bun packages/cli/src/index.ts journal --limit 10

# Expose the fixed HotRepl tool set over MCP stdio.
bun packages/mcp/src/index.ts
```

## Integration paths

| Path           | Use it for                                                   | Entry point                                          |
| -------------- | ------------------------------------------------------------ | ---------------------------------------------------- |
| Raw eval       | Interactive inspection and one-off repair snippets           | `Session.eval()` or `hotrepl eval`                   |
| Typed commands | Repeatable game automation, exports, and artifact collection | `Session.run()`, `commands_list`, `command_describe` |
| CLI            | Scripts and shell-driven local workflows                     | `packages/cli/src/index.ts`                          |
| MCP            | Agent tools with a small stable tool catalog                 | `packages/mcp/src/index.ts`                          |
| Host embedding | New Unity loader adapters or test hosts                      | `IReplHost` + `ReplEngine.Tick()`                    |

Minimal SDK usage:

```ts
import { connect } from "@hotrepl/sdk";

const session = await connect({ url: "ws://127.0.0.1:18590" });
const product = await session.eval("UnityEngine.Application.productName");
const preflight = await session.run("archive.preflight", {});

console.log(product.value, preflight.output);
```

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

## TypeScript and .NET packages

| Package                | Purpose                                                               |
| ---------------------- | --------------------------------------------------------------------- |
| `@hotrepl/protocol`    | Wire constants, TypeBox schemas, and message types                    |
| `@hotrepl/sdk`         | `connect`, `Session`, `Artifact`, typed errors, WebSocket transport   |
| `@hotrepl/testing`     | `FakeRuntime`, `MockSession`, recorder, and replay helpers (internal) |
| `@hotrepl/conformance` | Protocol conformance suite for FakeRuntime (internal)                 |
| `@hotrepl/cli`         | `hotrepl` command-line adapter over the SDK                           |
| `@hotrepl/mcp`         | fixed nine-tool MCP stdio server over the SDK                         |

Versions are managed by [changesets](https://github.com/changesets/changesets). The four publishable
packages (`@hotrepl/protocol`, `@hotrepl/sdk`, `@hotrepl/cli`, `@hotrepl/mcp`) are released to npm
under the `@hotrepl` org from `main`; `@hotrepl/testing` and `@hotrepl/conformance` stay
workspace-internal. Each release lands as a tagged GitHub Release whose body is the package's
CHANGELOG entry.

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

## Contributing

```bash
brew install lefthook dprint actionlint commitlint typos
bun install --frozen-lockfile
dotnet tool restore
lefthook install
lefthook run pre-push --force
```

`pre-push` mirrors CI: Bun install/tests/typecheck/schema export, dprint, typos, actionlint, C#
build, and C# tests. See [`AGENTS.md`](AGENTS.md) for agent-specific constraints and targeted
commands.

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

Publishing uses [npm trusted publishing via OIDC](https://docs.npmjs.com/trusted-publishers/) — no
long-lived `NPM_TOKEN`. Provenance is generated automatically.

## License

MIT
