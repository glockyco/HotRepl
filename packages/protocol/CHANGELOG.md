# @hotrepl/protocol

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

## 3.0.0

### Major Changes

- [`f6a4dcf`](https://github.com/glockyco/HotRepl/commit/f6a4dcf0e3a752914845f99b5aa00edbd7712d4b)
  Thanks [@glockyco](https://github.com/glockyco)! - Rebuild the protocol surface around TypeBox
  schemas and bring v2 validation in line with the runtime.

  - Every message type now ships with a paired `*Schema` constant. The existing exported type names
    remain (`EvalResultMessage`, `CommandDescriptor`, …) but are now
    `type X = Static<typeof
XSchema>` instead of `interface X`. Consumers that relied on declaration
    merging on these types need to wrap them.
  - `ServerMessage` now includes the new `AssemblyReloadMessage` variant. Exhaustive `switch` blocks
    over `ServerMessage` need a case for it.
  - The bundled JSON-Schema dependency moved from `@sinclair/typebox@0.34` to `typebox@1.x`. Schema
    constants are now stable to import directly.
  - `SubscribeMessageSchema` and `JobCancelResultMessageSchema` are corrected to match the runtime
    shape (`onChange`, `timeoutMs`, required `jobId`).

  Migration:

  ```ts
  // before
  import type { EvalResultMessage } from "@hotrepl/protocol";

  // after — same import, no source change required unless you used declaration merging
  import type { EvalResultMessage } from "@hotrepl/protocol";

  // new: import the matching schema for runtime validation
  import { EvalResultMessageSchema } from "@hotrepl/protocol";
  ```
