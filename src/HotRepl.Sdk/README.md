# HotRepl.Sdk

The first-party C# SDK for [HotRepl](https://github.com/glockyco/HotRepl) — a runtime C# REPL and
typed command bridge for Unity games. Use it from .NET build tools, automation, and tests to drive a
running Unity game that exposes the HotRepl plugin (BepInEx/Mono) or mod (MelonLoader/IL2CPP).

## Requirements

- .NET Standard 2.0 consumer (targets `netstandard2.0`).
- A running Unity game with the HotRepl host loaded. Default endpoint is `ws://127.0.0.1:18590`.

## Quickstart

```csharp
using HotRepl.Control;
using HotRepl.Sdk;

var client = new HotReplClient(new Uri("ws://127.0.0.1:18590"));
await using var session = await client.ConnectAsync();

var preflight = await session.RunAsync<EmptyArgs, Preflight>(
    "compendium.preflight",
    new EmptyArgs());
Console.WriteLine($"writable={preflight.Output.Writable}");

var job = await session.StartJobAsync<ExportArgs, ExportResult>(
    "compendium.export",
    new ExportArgs { Screenshots = true });
await foreach (var progress in job.ProgressAsync())
{
    Console.WriteLine(progress.Message);
}
var done = await job.WaitForCompletionAsync();
```

`RunAsync` and `StartJobAsync` throw `HotReplCommandException` on a `failed` command result and
`HotReplJobFailedException` on a failed job. The session caches the command catalog, so a call
dispatches on the cached descriptor without an extra `command_describe` round-trip.

## Reference

- Repository and protocol reference:
  [github.com/glockyco/HotRepl](https://github.com/glockyco/HotRepl).
- Protocol surface:
  [`docs/control-plane-protocol.md`](https://github.com/glockyco/HotRepl/blob/main/docs/control-plane-protocol.md).
- Authoring typed commands for a game host:
  [`docs/authoring-commands.md`](https://github.com/glockyco/HotRepl/blob/main/docs/authoring-commands.md).
- Test harness for command handlers:
  [`HotRepl.Testing`](https://github.com/glockyco/HotRepl/tree/main/src/HotRepl.Testing).
- Sibling TypeScript SDK: [`@hotrepl/sdk`](https://www.npmjs.com/package/@hotrepl/sdk).

## License

MIT
