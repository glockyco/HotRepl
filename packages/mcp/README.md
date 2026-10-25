# @hotrepl/mcp

This MCP server gives a coding agent access to a running Unity game through
[HotRepl](https://github.com/glockyco/HotRepl). Use it to let the agent evaluate C#, discover game
commands and read exported artifacts through an MCP-compatible host.

[npm package](https://www.npmjs.com/package/@hotrepl/mcp) · [Website](https://hotrepl.glockyco.com)

## Setup

You need Node.js and npm, an MCP host that can launch a local stdio server, and a game running
HotRepl through BepInEx or Lunaris on Mono, or MelonLoader on IL2CPP. Follow the
[game setup instructions](https://github.com/glockyco/HotRepl#set-up-a-game) first.

Add a server entry to your MCP host's configuration. For hosts that use an `mcpServers` object:

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

The launcher downloads the package on first use. Follow your host's configuration location and
reload procedure rather than assuming all MCP hosts use the same format.

To connect to another endpoint, add an `env` entry to the HotRepl server configuration:

```json
"env": { "HOTREPL_URL": "ws://127.0.0.1:18591" }
```

## Inspect and automate a game

Start the game, then ask your agent to inspect the HotRepl runtime and list its commands. Have it
describe a command's schema before calling it. Use C# evaluation for exploration and typed commands
for repeatable tasks.

The [tool definitions](https://github.com/glockyco/HotRepl/blob/main/packages/mcp/src/tools.ts)
define the tool names, arguments and annotations. The server also exposes completion, evaluator
reset, journal queries and artifact reads. Tool calls report connection failures without requiring
the MCP server to restart when the game becomes available.

## Runtime access and files

The agent can submit C# and commands that change game state or write files with the game's
permissions. Only connect an agent you trust, and keep the game host on loopback because the
protocol has no authentication. Command annotations describe behavior but are not an authorization
boundary.

Artifact tools read game-created files and verify their hashes. Text artifacts return their contents
to the MCP host. Binary artifacts return file information rather than decoded image data.
Diagnostics go to stderr because stdout carries MCP messages.

A new client connection replaces the existing game session. Close competing CLI or MCP sessions if
your tools report a displaced session. If a call hangs in an infinite Mono loop, the game may need a
restart because a timeout cannot always interrupt it.

## Troubleshooting

If no tools appear, check the host's configuration and server logs, then follow its reload
procedure. If tools appear but cannot connect, check that the game is running and its loader log
reports a successful HotRepl startup.

## Reference and development

- [Protocol reference](https://github.com/glockyco/HotRepl/blob/main/docs/control-plane-protocol.md)
  describes runtime messages and artifact paths.
- [AGENTS.md](https://github.com/glockyco/HotRepl/blob/main/AGENTS.md) covers development setup and
  MCP checks.
- [TypeScript SDK](https://www.npmjs.com/package/@hotrepl/sdk) and
  [CLI](https://www.npmjs.com/package/@hotrepl/cli) provide programmatic and shell clients.

## License

[MIT](https://github.com/glockyco/HotRepl/blob/main/LICENSE).
