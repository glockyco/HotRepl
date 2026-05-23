# @hotrepl/mcp

[![npm](https://img.shields.io/npm/v/@hotrepl/mcp.svg)](https://www.npmjs.com/package/@hotrepl/mcp)
[![license](https://img.shields.io/npm/l/@hotrepl/mcp.svg)](https://github.com/glockyco/HotRepl/blob/main/LICENSE)

[Model Context Protocol](https://modelcontextprotocol.io) stdio server for
[HotRepl](https://github.com/glockyco/HotRepl). Wire it into any MCP-compatible coding agent so the
agent can eval C#, invoke typed commands, and inspect a running Unity game on the user's behalf.

## Requirements

- A Unity game running the HotRepl plugin (BepInEx/Mono) or mod (MelonLoader/IL2CPP). The plugin
  opens `ws://127.0.0.1:18590` by default.
- Node (the published binary uses `#!/usr/bin/env node`).
- An MCP-compatible host application.

## Setup

Add an entry to your MCP host's server configuration. The launcher (`npx -y @hotrepl/mcp`) downloads
on first run and caches between launches; no global install is required.

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

To point at a non-default backend, pass `HOTREPL_URL` via the `env` block:

```json
{
  "mcpServers": {
    "hotrepl": {
      "command": "npx",
      "args": ["-y", "@hotrepl/mcp"],
      "env": { "HOTREPL_URL": "ws://127.0.0.1:18591" }
    }
  }
}
```

Restart the host application after editing its config. Consult your host's documentation for the
exact config-file location and reload procedure.

## Tools exposed

The server registers exactly nine tools — a deliberately small surface so agents can keep the full
catalog in context.

| Tool                       | Description                                          | Annotations   |
| -------------------------- | ---------------------------------------------------- | ------------- |
| `hotrepl_info`             | Return runtime handshake and capability information. | readOnly      |
| `hotrepl_eval`             | Evaluate C# code in the runtime.                     | —             |
| `hotrepl_complete`         | Return completions for C# code.                      | readOnly      |
| `hotrepl_reset`            | Reset evaluator state.                               | —             |
| `hotrepl_list_commands`    | List typed HotRepl commands.                         | readOnly      |
| `hotrepl_describe_command` | Describe one typed HotRepl command.                  | readOnly      |
| `hotrepl_run`              | Run a typed HotRepl command by name.                 | destructive\* |
| `hotrepl_read_artifact`    | Read and verify a HotRepl artifact reference.        | readOnly      |
| `hotrepl_journal`          | Query recent eval and command journal entries.       | readOnly      |

\* `hotrepl_run` starts with conservative MCP-spec defaults (`destructiveHint: true`). Once the
server reaches the backend, a background refresh narrows the annotation to match the actual command
set (`readOnlyHint: true` when all registered commands are read-only) and notifies the host via
`notifications/tools/list_changed`.

## Behaviour

- **Boot-time safety.** Tool registration is synchronous — the server starts cleanly even when the
  game isn't running yet. Tool calls return a structured `isError` envelope ("HotRepl is not
  reachable…") instead of crashing.
- **Spec-compliant shutdown.** stdin EOF (the canonical MCP shutdown signal), `SIGTERM`, and
  `SIGINT` all trigger graceful shutdown. A 2-second watchdog guarantees the process exits even if
  the backend hangs.
- **stderr-only diagnostics.** Per MCP spec, stdout is reserved for JSON-RPC; any human-readable
  error goes to stderr.

## Troubleshooting

| Symptom                                              | Likely cause / fix                                                                                                                                                                                                    |
| ---------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Host doesn't show HotRepl's tools                    | Most often a JSON syntax error in the host's MCP config (missing comma, mismatched bracket). Any syntax error typically disables every server silently. Re-validate the file, then fully quit and re-launch the host. |
| Tools call returns "HotRepl is not reachable…"       | The Unity game isn't running, or doesn't have the HotRepl plugin/mod loaded. Start the game; the next tool call will succeed.                                                                                         |
| Tools call returns "Session was evicted: displaced." | Another client connected and replaced this one. HotRepl is single-client. Close other CLIs/MCP sessions or accept the eviction; the next tool call reconnects.                                                        |
| Eval/run hangs for >30 s                             | The Mono.CSharp evaluator may have hit a tight loop. `hotrepl_reset` can recover; a stuck game may need a restart.                                                                                                    |

## Reference

- Repository, full docs, and examples:
  [github.com/glockyco/HotRepl](https://github.com/glockyco/HotRepl)
- Protocol reference:
  [`docs/control-plane-protocol.md`](https://github.com/glockyco/HotRepl/blob/main/docs/control-plane-protocol.md)
- Sibling packages: [`@hotrepl/sdk`](https://www.npmjs.com/package/@hotrepl/sdk) for programmatic
  use, [`@hotrepl/cli`](https://www.npmjs.com/package/@hotrepl/cli) for shell usage.
- Issues: [github.com/glockyco/HotRepl/issues](https://github.com/glockyco/HotRepl/issues)

## License

MIT
