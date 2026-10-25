# @hotrepl/protocol

This package provides TypeScript types and schemas for the
[HotRepl](https://github.com/glockyco/HotRepl) WebSocket protocol. Use it when you build a custom
client, generate bindings or validate game-runtime messages. For normal TypeScript automation, use
[@hotrepl/sdk](https://www.npmjs.com/package/@hotrepl/sdk) instead.

[npm package](https://www.npmjs.com/package/@hotrepl/protocol) ·
[Protocol reference](https://hotrepl.glockyco.com/protocol/)

## Install

```sh
npm install @hotrepl/protocol
```

You do not need a running game to use the types and schemas. Connecting a custom client requires a
game running a HotRepl host. See the
[game setup instructions](https://github.com/glockyco/HotRepl#set-up-a-game).

## Use types and schemas

Import types, message discriminants and schema objects from the package root:

```ts
import {
  type HandshakeMessage,
  HandshakeMessageSchema,
  MESSAGE_TYPES,
  PROTOCOL_VERSION,
} from "@hotrepl/protocol";

function summarizeHandshake(message: HandshakeMessage): string {
  return `${message.host.name}: protocol ${message.protocolVersion}`;
}

console.log(MESSAGE_TYPES.handshake, PROTOCOL_VERSION);
console.log(HandshakeMessageSchema);
```

TypeScript types do not validate incoming JSON. Use the exported schema objects with a compatible
validator before treating external data as typed messages. The
[source exports](https://github.com/glockyco/HotRepl/blob/main/packages/protocol/src/index.ts)
define the available types, schemas and constants.

The package also ships generated JSON Schema files. Its
[package manifest](https://github.com/glockyco/HotRepl/blob/main/packages/protocol/package.json)
exports only the package root, so do not import JSON files through a `@hotrepl/protocol/schemas/`
subpath. Import schema objects from the root or obtain standalone files from the installed package
for tools that need files.

## Generate schemas and change the protocol

From the repository root, regenerate standalone schemas after changing their source:

```sh
bun run schemas:export
```

The
[export script](https://github.com/glockyco/HotRepl/blob/main/packages/protocol/scripts/export-schemas.ts)
defines the output location and schema format. The package itself does not connect to a game or
change game files.

The
[protocol reference](https://github.com/glockyco/HotRepl/blob/main/docs/control-plane-protocol.md)
explains message semantics. [AGENTS.md](https://github.com/glockyco/HotRepl/blob/main/AGENTS.md)
covers development setup, protocol changes and checks. Keep the C# protocol implementation and
TypeScript schemas aligned when changing a message.

## License

[MIT](https://github.com/glockyco/HotRepl/blob/main/LICENSE).
