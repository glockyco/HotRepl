# @hotrepl/sdk

[![npm](https://img.shields.io/npm/v/@hotrepl/sdk.svg)](https://www.npmjs.com/package/@hotrepl/sdk)
[![license](https://img.shields.io/npm/l/@hotrepl/sdk.svg)](https://github.com/glockyco/HotRepl/blob/main/LICENSE)

The official TypeScript SDK for [HotRepl](https://github.com/glockyco/HotRepl) — a runtime C# REPL
and typed command bridge for Unity games. Use it to inspect, automate, or export data from any Unity
game running the HotRepl plugin (BepInEx/Mono) or mod (MelonLoader/IL2CPP).

## Requirements

- A Unity game running the HotRepl plugin (`HotRepl.BepInEx.dll`) or mod
  (`HotRepl.Host.MelonLoader.dll`). The plugin opens `ws://127.0.0.1:18590` by default.
- A modern JavaScript runtime (Bun or Node) with WebSocket support.

## Install

```bash
bun add @hotrepl/sdk           # or: npm install @hotrepl/sdk
```

## Quickstart

```ts
import { connect } from "@hotrepl/sdk";

const session = await connect(); // ws://127.0.0.1:18590 by default

// Raw eval — any C# expression, on the game's main thread:
const product = await session.eval<string>("UnityEngine.Application.productName");
// → { hasValue: true, value: "Ardenfall", valueType: "System.String", durationMs: 7 }

// Typed, schema-validated game command:
const preflight = await session.run<{ writable: boolean; freeMb: number }>(
  "archive.preflight",
  {},
);
// → { output: { writable: true, freeMb: 41213 }, artifacts: {} }

session.close();
```

Point at a non-default backend with `connect({ url: "ws://host:port" })` or by setting `HOTREPL_URL`
in the environment.

## What you get

- `connect(options?)` — open a WebSocket session against the game.
- `Session.eval(code, timeoutMs?)` — evaluate C# on the main thread.
- `Session.run(name, args, options?)` — invoke a typed, schema-validated command registered by the
  host or a game mod.
- `Session.complete`, `Session.reset`, `Session.journal`, `Session.watch` — completion, evaluator
  reset, evaluation/command history, and frame-by-frame value streaming.
- `Session.close()` — release the underlying WebSocket. Long-running consumers (servers, daemons)
  should call this on shutdown.
- Typed errors: `HotReplError`, `HotReplSessionEvicted`, `HotReplArtifactCorrupted`.

## Reference

- Repository, full docs, and examples:
  [github.com/glockyco/HotRepl](https://github.com/glockyco/HotRepl)
- Protocol reference:
  [`docs/control-plane-protocol.md`](https://github.com/glockyco/HotRepl/blob/main/docs/control-plane-protocol.md)
- Sibling packages: [`@hotrepl/cli`](https://www.npmjs.com/package/@hotrepl/cli) for shell usage,
  [`@hotrepl/mcp`](https://www.npmjs.com/package/@hotrepl/mcp) for agent tooling.
- Issues: [github.com/glockyco/HotRepl/issues](https://github.com/glockyco/HotRepl/issues)

## License

MIT
