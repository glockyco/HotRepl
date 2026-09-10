---
"@hotrepl/sdk": patch
"@hotrepl/cli": patch
"@hotrepl/mcp": patch
---

Deliver command artifacts as files a client can read.

Every command artifact was written by `InMemoryArtifactWriter`, so a reference carried the URI
`hotrepl-artifact://memory/<name>` and no path. `unity.screenshot.capture` reported a 1.8 MB PNG
that no client could open: the SDK fell through to `fetch()` and threw
`protocol must be http:, https: or s3:`. The engine now writes attachments into
`ReplConfig.ArtifactDirectory`, so the reference carries a path.

The clients no longer corrupt or hide what they read. `WebSocketTransport` reads a `file:` URI, uses
`node:fs` instead of Bun-only APIs, and reports an unreadable scheme as
`artifact_missing/artifactNotReadable` rather than a native `TypeError`. `hotrepl artifacts read`
gained `--output <file>` for bytes, because decoding a PNG as UTF-8 text destroys it, and the MCP
`hotrepl_read_artifact` tool returns location metadata for non-text content instead of mojibake.
`connect` accepts `resolveArtifactPath` so a client can open an artifact from a game whose paths
belong to another namespace, such as a Wine or Proton bottle.
