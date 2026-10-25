# @hotrepl/cli

This command-line client lets you inspect and automate a running Unity game through
[HotRepl](https://github.com/glockyco/HotRepl). Use it from your shell to evaluate C#, invoke game
commands, inspect the journal and read exported artifacts.

[npm package](https://www.npmjs.com/package/@hotrepl/cli) · [Website](https://hotrepl.glockyco.com)

## Setup

You need Node.js and npm, plus a game running a HotRepl host through BepInEx or Lunaris on Mono, or
MelonLoader on IL2CPP. Follow the
[game setup instructions](https://github.com/glockyco/HotRepl#set-up-a-game) first. Installing this
package does not install a game plugin.

Check the connection without a global installation:

```sh
npx -y @hotrepl/cli info
npx -y @hotrepl/cli eval 'UnityEngine.Application.productName'
```

For repeated use, install the `hotrepl` command:

```sh
npm install -g @hotrepl/cli
hotrepl info
```

## Inspect and call a game command

```sh
hotrepl describe unity.app.info --json
hotrepl run unity.app.info '{}'
hotrepl journal --limit 10 --json
```

Use `describe --json` to see the argument schema before calling a command. Text output shows only
its name, version, kind and state-mutation flag. Use `eval` for one-off C# exploration and `reset`
to clear evaluator state.

Pass `--url ws://127.0.0.1:18591` or set `HOTREPL_URL` to connect to a different endpoint. The
[argument parser](https://github.com/glockyco/HotRepl/blob/main/packages/cli/src/index.ts) defines
output and connection options. The
[command dispatcher](https://github.com/glockyco/HotRepl/blob/main/packages/cli/src/commands/dispatch.ts)
defines the available commands.

## Wait for the game and watch values

```sh
hotrepl wait --timeout-ms 60000
hotrepl watch 'UnityEngine.Time.frameCount' --jsonl
```

`hotrepl wait` retries the connection until the host completes its handshake, so a script can start
before the game is ready. `--timeout-ms` changes the deadline that
[the wait implementation](https://github.com/glockyco/HotRepl/blob/main/packages/cli/src/commands/wait.ts)
sets by default. When the deadline expires, the command returns the timeout status from the
exit-code mapping.

`hotrepl watch` writes each value as it arrives. `--jsonl` writes one JSON object per tick, and
`--json` streams a JSON array that closes when the watch ends or is interrupted. Press Ctrl+C once
to close the session and finish writing output, and again to exit immediately if cleanup stalls.

## Read an artifact

Run a command with `--json` to obtain its artifact references. Pass one complete reference as JSON
to `hotrepl artifacts read`. Text artifacts print to the terminal by default. Add `--output` for
binary data or to save verified bytes:

```sh
hotrepl artifacts read '<artifact-reference-json>' --output ./artifact.bin
```

Replace `<artifact-reference-json>` with the reference returned by your command. The output option
writes or overwrites the named local file. Artifact reads require access to the path reported by the
game. See the
[artifact reference](https://github.com/glockyco/HotRepl/blob/main/docs/control-plane-protocol.md#artifacts)
for file lifetime and Wine or Proton path mapping.

## Runtime access and connection failures

Evaluation and commands run with the game's permissions and can change game state or write files.
Keep the host on loopback because the protocol has no authentication.

If the client cannot connect, start the game and check its loader log for HotRepl startup errors.
Another CLI or MCP connection replaces the current client, so close other clients when a session is
displaced. The
[exit-code mapping](https://github.com/glockyco/HotRepl/blob/main/packages/cli/src/exit-codes.ts)
defines failure statuses for scripts.

## Reference and development

- [Protocol reference](https://github.com/glockyco/HotRepl/blob/main/docs/control-plane-protocol.md)
  describes requests, results and artifacts.
- [AGENTS.md](https://github.com/glockyco/HotRepl/blob/main/AGENTS.md) covers development setup and
  CLI checks.
- [TypeScript SDK](https://www.npmjs.com/package/@hotrepl/sdk) and
  [MCP server](https://www.npmjs.com/package/@hotrepl/mcp) provide programmatic and coding-agent
  access.

## License

[MIT](https://github.com/glockyco/HotRepl/blob/main/LICENSE).
