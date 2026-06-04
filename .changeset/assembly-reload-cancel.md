---
"@hotrepl/sdk": minor
"@hotrepl/testing": minor
---

Handle assembly reloads and cancellation in the SDK. `Session.onAssemblyReload` surfaces hot-reload
pushes that were previously dropped and invalidates the cached command catalog and descriptors so
stale schemas are not reused. `Session.cancel(targetId)` cancels an active eval or subscription, and
`watch()` now cancels its server subscription automatically when the iterator stops before `final`.
`FakeRuntime` gains matching helpers (`onAssemblyReload`/`emitAssemblyReload`,
`cancel`/`cancelled`).
