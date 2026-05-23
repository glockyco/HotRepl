# @hotrepl/cli

[![npm](https://img.shields.io/npm/v/@hotrepl/cli.svg)](https://www.npmjs.com/package/@hotrepl/cli)
[![license](https://img.shields.io/npm/l/@hotrepl/cli.svg)](https://github.com/glockyco/HotRepl/blob/main/LICENSE)

Command-line interface for [HotRepl](https://github.com/glockyco/HotRepl). Talk to a running Unity
game from your shell — eval C#, invoke typed commands, inspect the journal, stream watch frames,
read artifacts.

## Requirements

- A Unity game running the HotRepl plugin (BepInEx/Mono) or mod (MelonLoader/IL2CPP). The plugin
  opens `ws://127.0.0.1:18590` by default.
- Node (the published binary uses `#!/usr/bin/env node`).

## Run it without installing

```bash
bunx @hotrepl/cli info               # or: npx -y @hotrepl/cli info
bunx @hotrepl/cli eval 'UnityEngine.Application.productName'
```

## Install globally

```bash
bun add -g @hotrepl/cli              # or: npm install -g @hotrepl/cli
hotrepl info
```

## Commands

```text
hotrepl info                     # handshake summary
hotrepl wait                     # block until backend is reachable
hotrepl doctor                   # quick self-check
hotrepl eval '<C# expr>'         # evaluate C# on the game's main thread
hotrepl reset                    # reset evaluator state
hotrepl complete '<prefix>' [N]  # completions for partial C#
hotrepl run <name> '<json args>' # invoke a typed command
hotrepl describe <name>          # show a command's schema
hotrepl artifacts read <ref>     # read and verify an artifact
hotrepl journal [--limit N]      # recent eval/command history
hotrepl watch '<C# expr>'        # stream frame-by-frame values
```

Global flags: `--format text|json|jsonl`, `--json` (alias), `--jsonl` (alias),
`--url ws://host:port`, `--limit N`.

## Environment

| Variable      | Effect                                        |
| ------------- | --------------------------------------------- |
| `HOTREPL_URL` | Backend URL (default `ws://127.0.0.1:18590`). |

## Exit codes

The CLI follows [sysexits.h](https://www.man7.org/linux/man-pages/man3/sysexits.h.3head.html): `0`
success, `2` validation/invalid request, `69` backend unreachable, `70` internal failure, `75`
session evicted, `76` artifact corrupted. Full mapping in
[`packages/cli/src/exit-codes.ts`](https://github.com/glockyco/HotRepl/blob/main/packages/cli/src/exit-codes.ts).

## Reference

- Repository: [github.com/glockyco/HotRepl](https://github.com/glockyco/HotRepl)
- Protocol reference:
  [`docs/control-plane-protocol.md`](https://github.com/glockyco/HotRepl/blob/main/docs/control-plane-protocol.md)
- Sibling packages: [`@hotrepl/sdk`](https://www.npmjs.com/package/@hotrepl/sdk) for programmatic
  use, [`@hotrepl/mcp`](https://www.npmjs.com/package/@hotrepl/mcp) for agent tooling.
- Issues: [github.com/glockyco/HotRepl/issues](https://github.com/glockyco/HotRepl/issues)

## License

MIT
