---
"@hotrepl/protocol": major
"@hotrepl/sdk": major
---

Eval and subscription results now return properly typed output. `value` is emitted as native JSON
instead of a JSON-encoded string, `valueType` carries the .NET type name, and a `truncated` /
`truncatedBytes` pair signals when a result exceeds `maxResultLength` (in which case `value` is
`null` rather than partial, invalid JSON).

This is a breaking change for consumers that previously parsed `value` a second time.
`Session.eval<T>()` and `Session.watch<T>()` now return the typed value directly and expose
`truncated` / `truncatedBytes`.
