# @hotrepl/protocol

[![npm](https://img.shields.io/npm/v/@hotrepl/protocol.svg)](https://www.npmjs.com/package/@hotrepl/protocol)
[![license](https://img.shields.io/npm/l/@hotrepl/protocol.svg)](https://github.com/glockyco/HotRepl/blob/main/LICENSE)

TypeScript types and JSON schemas for the [HotRepl](https://github.com/glockyco/HotRepl) WebSocket
protocol — the wire format a HotRepl-instrumented Unity game speaks to its clients (SDK, CLI, MCP
server, custom integrations).

Most consumers should depend on [`@hotrepl/sdk`](https://www.npmjs.com/package/@hotrepl/sdk)
instead. Use this package directly only when you are building an alternative SDK, a non-TypeScript
client, or a tool that needs the schemas (e.g., a code generator or a test fixture validator).

## Install

```bash
bun add @hotrepl/protocol      # or: npm install @hotrepl/protocol
```

## What's in the box

- **TypeScript types** for every protocol message (`HandshakeMessage`, `EvalResultMessage`,
  `CommandCallMessage`, `JobAcceptedMessage`, `SessionEvictedMessage`, …).
- **Discriminant constants** — `MESSAGE_TYPES`, `PROTOCOL_VERSION`, `ERROR_KINDS`, `defaultLimits`.
- **JSON Schema files** for every public message, exported under `schemas/` and generated from the
  same TypeScript source via `bun run schemas:export`.

## Usage

```ts
import { type HandshakeMessage, MESSAGE_TYPES, PROTOCOL_VERSION } from "@hotrepl/protocol";

function isHandshake(message: { type: string }): message is HandshakeMessage {
  return message.type === MESSAGE_TYPES.handshake;
}
```

Schemas (Draft 2020-12) live alongside the types if you need to validate wire frames:

```ts
import handshakeSchema from "@hotrepl/protocol/schemas/handshake.schema.json"
  with { type: "json" };
```

## Reference

- Full protocol reference:
  [`docs/control-plane-protocol.md`](https://github.com/glockyco/HotRepl/blob/main/docs/control-plane-protocol.md)
- Repository: [github.com/glockyco/HotRepl](https://github.com/glockyco/HotRepl)
- Issues: [github.com/glockyco/HotRepl/issues](https://github.com/glockyco/HotRepl/issues)

## License

MIT
