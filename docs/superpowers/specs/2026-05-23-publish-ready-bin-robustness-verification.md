# Bin robustness verification results

Executed against tarballs built from the implementation commits, installed into `/tmp/hotrepl-smoke`
via `npm install --no-fund`.

## Environment

- Node: v25.9.0 (used for tarball smoke tests)
- Bun: 1.3.14 (used for unit tests)
- Platform: macOS arm64

## Unit test suite

```
bun test packages/sdk packages/testing packages/mcp packages/cli
42 pass, 0 fail across 9 files
```

## Step 1: Build and pack

All four publishable packages built cleanly (`bun run build`) and `npm pack` produced tarballs in
`/tmp/hotrepl-pack/`:

- `hotrepl-protocol-2.0.0.tgz`
- `hotrepl-sdk-2.0.0.tgz`
- `hotrepl-cli-2.0.0.tgz`
- `hotrepl-mcp-2.0.0.tgz`

`npm install --no-fund` in a fresh `/tmp/hotrepl-smoke` resolved all four plus transitive
dependencies (97 packages). Both bins have `#!/usr/bin/env node` shebangs and executable bits.

## Step 2: Unreachable-backend MCP smoke

Command:

```
(
  printf initialize JSON-RPC...
  printf notifications/initialized...
  printf tools/list...
  printf tools/call hotrepl_eval {"code":"1+1"}...
  sleep 1
) | HOTREPL_URL=ws://127.0.0.1:1 node node_modules/.bin/hotrepl-mcp
```

Results:

- stdout: 3 JSON-RPC responses (initialize result, tools/list with 9 tools, tools/call result)
- `hotrepl_run` annotations in tools/list: `destructiveHint: true,
  readOnlyHint: false`
  (conservative defaults)
- `tools/call hotrepl_eval` response: `isError: true`, content text contains "HotRepl is not
  reachable at the configured URL. Make sure your Unity game with the HotRepl plugin (BepInEx) or
  mod (MelonLoader) is running."
- stderr: empty (no stack trace, no `Error:` line)
- exit: 0 (stdin EOF closed transport cleanly)

## Step 3: Unreachable-backend CLI smoke

Command:

```
HOTREPL_URL=ws://127.0.0.1:1 node node_modules/.bin/hotrepl info --json
```

Results:

- stderr: `HotRepl WebSocket connection failed.` (one line, no stack trace)
- stdout: empty
- exit: 69 (EX_UNAVAILABLE)

## Steps 4–8: Live Ardenfall verification

Not executed in this session — requires the Ardenfall Demo game running with the BepInEx HotRepl
plugin loaded. The `~/Projects/ardenfall-compendium` consumer project provides
`bun run hotrepl:setup` and `bun run hotrepl:launch` to prepare the environment. The verification
matrix (eval, run, SIGTERM, SIGINT) should be run separately once the game is available.
