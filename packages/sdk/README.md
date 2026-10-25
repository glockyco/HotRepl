# @hotrepl/sdk

This TypeScript SDK connects scripts and applications to a running Unity game through
[HotRepl](https://github.com/glockyco/HotRepl). Use it to evaluate C#, call game commands and read
exported artifacts without implementing the WebSocket protocol yourself.

[npm package](https://www.npmjs.com/package/@hotrepl/sdk) · [Website](https://hotrepl.glockyco.com)

## Setup

You need a JavaScript runtime with WebSocket support and a game running a HotRepl host. The host
supports BepInEx or Lunaris on Mono and MelonLoader on IL2CPP. Follow the
[game setup instructions](https://github.com/glockyco/HotRepl#set-up-a-game) before connecting.

```sh
npm install @hotrepl/sdk
```

## Inspect and automate a game

```ts
import { connect } from "@hotrepl/sdk";

const session = await connect();
try {
  const product = await session.eval<string>("UnityEngine.Application.productName");
  console.log(product.value);
  const app = await session.run("unity.app.info", {});
  console.log(app.output);
} finally {
  session.close();
}
```

Use `session.eval()` for C# exploration and `session.run()` for registered, schema-validated
commands. Close the session when your script or service finishes so its WebSocket does not keep the
process running.

## Choose an endpoint

Pass an endpoint directly with `connect({ url: "ws://127.0.0.1:18591" })`. To read `HOTREPL_URL`
from a Node or Bun process, supply its environment explicitly:

```ts
const session = await connect({ env: process.env });
```

`connect()` does not read process environment variables on its own. An explicit `url` takes
precedence over `env.HOTREPL_URL`. The
[connection implementation](https://github.com/glockyco/HotRepl/blob/main/packages/sdk/src/connect.ts)
defines the fallback endpoint and supported options, including artifact path mapping.

To give up on a host that does not answer, pass an `AbortSignal` as `connect({ signal })`.
Cancellation covers the wait for the handshake, closes the pending transport and rejects with the
signal's reason. Once `connect()` resolves, the signal no longer affects the session, so call
`session.close()` when you finish.

## Runtime access and artifacts

Evaluation and commands execute with the game's permissions and can change state or write files.
Only send code and commands you trust, and keep the game host on loopback because the protocol has
no authentication. Connecting another client replaces your session.

Command results include artifact references. Use `session.artifact(ref)` for an existing reference,
or read artifacts from a command result, to access files and verify their hashes. The
[protocol reference](https://github.com/glockyco/HotRepl/blob/main/docs/control-plane-protocol.md#artifacts)
explains file lifetime and path mapping under Wine or Proton.

## Reference and development

- [Session API](https://github.com/glockyco/HotRepl/blob/main/packages/sdk/src/session.ts) defines
  evaluation, command calls, watches, journal queries and notifications.
- [Command authoring](https://github.com/glockyco/HotRepl/blob/main/docs/authoring-commands.md)
  explains how to expose your own game commands.
- [AGENTS.md](https://github.com/glockyco/HotRepl/blob/main/AGENTS.md) covers development setup and
  targeted SDK checks.
- [CLI](https://www.npmjs.com/package/@hotrepl/cli) and
  [MCP server](https://www.npmjs.com/package/@hotrepl/mcp) provide shell and coding-agent clients.

## License

[MIT](https://github.com/glockyco/HotRepl/blob/main/LICENSE).
