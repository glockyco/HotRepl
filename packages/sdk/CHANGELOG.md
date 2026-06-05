# @hotrepl/sdk

## 4.0.1

### Patch Changes

- [#6](https://github.com/glockyco/HotRepl/pull/6)
  [`9199584`](https://github.com/glockyco/HotRepl/commit/919958438318a1cfc03fca37d25b7ce0b2200b8c)
  Thanks [@glockyco](https://github.com/glockyco)! - Fix Bun package exports in published npm
  packages.

  The 4.0.0/3.0.1 packages declared a Bun-specific export target of `./src/index.ts`, but npm
  packages only published `dist/`. Bun prefers the `bun` export condition, so Bun consumers could
  not import `@hotrepl/sdk` or the other public packages from npm. Published Bun entrypoints now
  target the built `dist/index.js` files, and the test gate builds packages before tests so
  workspace package imports exercise the same public entrypoint shape.

- Updated dependencies
  [[`9199584`](https://github.com/glockyco/HotRepl/commit/919958438318a1cfc03fca37d25b7ce0b2200b8c)]:
  - @hotrepl/protocol@4.0.1

## 4.0.0

### Major Changes

- [#2](https://github.com/glockyco/HotRepl/pull/2)
  [`5725a51`](https://github.com/glockyco/HotRepl/commit/5725a51cef3db2b46475c6d007b94b1de6b742e7)
  Thanks [@glockyco](https://github.com/glockyco)! - Eval and subscription results now return
  properly typed output. `value` is emitted as native JSON instead of a JSON-encoded string,
  `valueType` carries the .NET type name, and a `truncated` / `truncatedBytes` pair signals when a
  result exceeds `maxResultLength` (in which case `value` is `null` rather than partial, invalid
  JSON).

  This is a breaking change for consumers that previously parsed `value` a second time.
  `Session.eval<T>()` and `Session.watch<T>()` now return the typed value directly and expose
  `truncated` / `truncatedBytes`.

### Minor Changes

- [#2](https://github.com/glockyco/HotRepl/pull/2)
  [`74d2b68`](https://github.com/glockyco/HotRepl/commit/74d2b68069ceaa502f8349aee439b2b4548e90b0)
  Thanks [@glockyco](https://github.com/glockyco)! - Handle assembly reloads and cancellation in the
  SDK. `Session.onAssemblyReload` surfaces hot-reload pushes that were previously dropped and
  invalidates the cached command catalog and descriptors so stale schemas are not reused.
  `Session.cancel(targetId)` cancels an active eval or subscription, and `watch()` now cancels its
  server subscription automatically when the iterator stops before `final`. `FakeRuntime` gains
  matching helpers (`onAssemblyReload`/`emitAssemblyReload`, `cancel`/`cancelled`).

### Patch Changes

- Updated dependencies
  [[`5725a51`](https://github.com/glockyco/HotRepl/commit/5725a51cef3db2b46475c6d007b94b1de6b742e7)]:
  - @hotrepl/protocol@4.0.0

## 3.0.0

### Major Changes

- [`f6a4dcf`](https://github.com/glockyco/HotRepl/commit/f6a4dcf0e3a752914845f99b5aa00edbd7712d4b)
  Thanks [@glockyco](https://github.com/glockyco)! - Add an explicit lifecycle and a catalog cache
  to `Session`.

  - `RuntimeTransport` now requires a `close(): void` method so the SDK can release the underlying
    WebSocket when a long-running process exits. Any custom `RuntimeTransport` implementation must
    add a `close()` member.
  - `Session.close()` is now public. Long-running consumers (servers, daemons, MCP) should call it
    on shutdown so the Node/Bun event loop can drain.
  - `Session.run(name, args)` now uses a per-session catalog cache (`Session.listCommands()` and
    `Session.getCatalogEntry(name)`) and no longer issues a `command_describe` per call. The catalog
    is fetched on first use and reused for the lifetime of the session.
  - Suppress the `undici` `uncaughtException` that fired when the underlying WebSocket failed to
    open; the resulting `HotReplConnectionError` is the only surfaced failure.

  Migration:

  ```ts
  // custom transports must now implement close()
  class MyTransport implements RuntimeTransport {
    // ...existing members
    close(): void {
      /* release your socket here */
    }
  }

  // long-running consumers should explicitly close
  const session = await connect();
  try {
    /* … */
  } finally {
    session.close();
  }
  ```

### Patch Changes

- Updated dependencies
  [[`f6a4dcf`](https://github.com/glockyco/HotRepl/commit/f6a4dcf0e3a752914845f99b5aa00edbd7712d4b)]:
  - @hotrepl/protocol@3.0.0
