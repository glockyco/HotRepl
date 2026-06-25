---
title: "Typed commands — Phase 4 consolidation design"
type: spec
status: implemented
created: 2026-05-24
parent:
superseded_by:
archived: 2026-06-25
---

# Typed commands — Phase 4 consolidation design

Status: **draft, awaiting implementation approval**

This spec defines the v4 redesign of HotRepl's typed-command authoring surface. The wire protocol
stays stable; the changes target the C# core API, the artifact subsystem, schema caching, and the
SDK surface (TypeScript and C#).

## Why

Two real consumers have shipped typed commands (Ardenfall under v3, Ancient Kingdoms under v3). A
side-by-side audit of both, plus a domain-expert architectural review, surfaced one dominant cost:
**every new consumer reimplements a parallel WebSocket protocol client because there is no
first-party C# SDK and the TypeScript SDK is not adopted everywhere**.

The duplication is measurable:

| Consumer         | Hand-rolled client                                                                       | LOC | Language   | Runtime                      |
| ---------------- | ---------------------------------------------------------------------------------------- | --- | ---------- | ---------------------------- |
| Ardenfall        | `controller/src/hotrepl-client.ts`                                                       | 343 | TypeScript | Bun + Node (controller side) |
| Ancient Kingdoms | `build-tool/HotRepl/{HotReplExportRunner,IHotReplTransport,ClientWebSocketTransport}.cs` | 344 | C#         | net10.0 (build-tool side)    |
| HotRepl SDK      | `packages/sdk/src/session.ts`                                                            | 345 | TypeScript | reference shape              |

Both new consumers also independently invented helper wrappers for the same friction points in the
v3 core API:

| Concern                                                            | Ardenfall                                | Ancient Kingdoms                                    |
| ------------------------------------------------------------------ | ---------------------------------------- | --------------------------------------------------- |
| `<TOutput>` inference helper around `ControlCommandResult.*Failed` | `CompendiumCommandResults.cs` (56 lines) | inline long-form, repeated 6× in `ExportJobCommand` |
| `ArtifactRef.FromFile`-equivalent                                  | `CompendiumCommandResults.FileArtifact`  | `ArtifactCollector.MakeRef`                         |
| Progress wrapper that hides the `JObject` snapshot construction    | not used in the surveyed handlers        | private `Progress(phase, message)`                  |
| 4-property handler header (`Name`/`Version`/`Kind`/`MutatesState`) | repeated × 8 handlers                    | repeated × 4 handlers                               |
| `ControlCommandKind.Synchronous` vs wire `"sync"` mismatch         | defensive parser in TS                   | tripped agents twice during the AK migration        |

The friction is API-shape friction, not runtime-specific friction. Ardenfall is **BepInEx 5 + Mono +
netstandard2.1 + nullable enabled**; Ancient Kingdoms is **MelonLoader + IL2CPP + net6.0 + nullable
disabled**. The boilerplate is the same in both.

A parallel investigation rejected the largest possible swing (re-baseline the wire to JSON-RPC 2.0 +
adopt StreamJsonRpc). Findings, in summary:

- StreamJsonRpc has mandatory dependencies on `MessagePack`, `Nerdbank.MessagePack`,
  `Nerdbank.Streams`, `Microsoft.VisualStudio.Threading.Only`, `System.IO.Pipelines`,
  `System.Text.Json`, `System.Collections.Immutable`, `System.Diagnostics.DiagnosticSource` — not
  optional, not per-formatter. Hostile to Unity Mono and IL2CPP load boundaries.
- Client-proxy generation uses `RequiresDynamicCode(RuntimeReasons.RefEmit)` paths by default;
  AOT-compatible source-generated proxies need analyzer plumbing in consumer builds, which
  BepInEx/MelonLoader HintPath workflows do not support cleanly.
- StreamJsonRpc has no JSON Schema contract generation; HotRepl's TS↔C# contract bridge via
  NJsonSchema would have to be reinvented separately.
- HotRepl's job semantics (server-owned addressable resource, accept → poll → terminal result +
  artifact envelope) do not map onto JSON-RPC notifications or `IAsyncEnumerable<T>` cleanly. We
  would still hand-roll job mechanics on top.

Similarly rejected: replacing NJsonSchema with `JsonSchema.Net.Generation` source generator. It is
System.Text.Json-centric, ignores Newtonsoft `[JsonProperty]` attributes, and ships under an Open
Source Maintenance Fee EULA. The actual NJsonSchema cost is self-inflicted (the validator reparses
schema JSON on every call) and is fixed without library churn.

The right cuts are surgical: ship the missing SDK, fix the core API where it forces boilerplate,
make file artifacts first-class. The wire stays.

## What we're shipping

Nine coordinated changes inside HotRepl, then two consumer-update plans (Ardenfall and AK) that
validate the design. Erenshor (originally Phase 4) is held until this lands and becomes Phase 5;
documentation moves to Phase 6.

| # | Change                                                                                                                       | Surface         | Breaking?                           |
| - | ---------------------------------------------------------------------------------------------------------------------------- | --------------- | ----------------------------------- |
| 1 | Cache compiled `JsonSchema` validators per command; correct the handshake `schemaValidation` capability                      | core            | no                                  |
| 2 | Rename `ControlCommandKind.Synchronous` → `Sync` (matches wire `"sync"`)                                                     | core            | **yes**                             |
| 3 | Generic-binding `ControlCommandContext<TOutput>` with instance `Ok`/`PreconditionFailed`/`ValidationFailed`/`Failed` helpers | core            | **yes**                             |
| 4 | Optional `[ControlCommand(...)]` attribute as alternative to four metadata properties (runtime-read, not source-generated)   | core            | no                                  |
| 5 | `IArtifactWriter.AttachFileAsync`/`AttachBytesAsync`/`AttachStreamAsync` + truthful `ArtifactsSchema` on descriptors         | core            | **yes** (IArtifactWriter signature) |
| 6 | New `HotRepl.Sdk` (netstandard2.0) — `HotReplClient`/`HotReplSession`/`HotReplJob`/`HotReplException`                        | new package     | additive                            |
| 7 | New `HotRepl.Testing` (netstandard2.0) — `HandlerHarness`, `ConformanceSuite` (C# sibling of the TS conformance package)     | new package     | additive                            |
| 8 | Cache `commands_list` per session in both SDKs; skip `command_describe` unless caller asks for schemas                       | TS SDK + C# SDK | **yes** (TS SDK behaviour change)   |
| 9 | Promote `HotRepl.UnityCommands.BepInEx`/`.MelonLoader` to canonical sample status; add `docs/authoring-commands.md`          | docs            | no                                  |

Two follow-on cross-repo plans:

| #  | Change                                                                                              | Repo                    |
| -- | --------------------------------------------------------------------------------------------------- | ----------------------- |
| 10 | Migrate Ardenfall: drop `hotrepl-client.ts`, drop `CompendiumCommandResults`, adopt new context API | `ardenfall-compendium`  |
| 11 | Migrate Ancient Kingdoms: delete `HotReplExportRunner.cs` + transport; adopt `HotRepl.Sdk`          | `ancient-kingdoms-mods` |

These follow-on plans are written **after** Phase 4 lands so they reflect the realized API. Phase 4
acceptance includes a passing Ardenfall + AK build against the new packages, but the consumer-side
cleanup commits ship in their own plans.

## Architecture decisions

### Decision 1 — Cache compiled `JsonSchema` validators per command

**Problem.** `NJsonSchemaValidator.Validate` reparses the schema JObject from text on every
invocation (`src/HotRepl.Core/Control/Schema/NJsonSchemaValidator.cs:14-29`). `SchemaCache.For<T>()`
caches the schema JObject (`src/HotRepl.Core/Control/Schema/SchemaCache.cs:13-56`) but the
compiled-validator side of NJsonSchema is recomputed on every call. This is the load that gets
blamed on "runtime reflection" but is actually re-parsing.

**Change.** Extend `SchemaCache` to additionally cache a compiled `NJsonSchema.JsonSchema` instance
per `Type`. `NJsonSchemaValidator` accepts a compiled schema directly; callers pass
`SchemaCache.For<T>()` for the JObject contract surface and `SchemaCache.CompiledFor<T>()` for
validation. Validators are reused for the lifetime of the process.

**Capability honesty.** The handshake currently advertises `schemaValidation: false`
(`src/HotRepl.Core/Server/RuntimeHandshakeFactory.cs:52-57`) despite the adapter validating input
args on every call (`src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs:54-67`). Set it to
`true`. If we ever add output-side validation later we'll split into
`inputValidation`/`outputValidation`, but right now one boolean truthfully reflects behaviour.

**Breaking?** No. Internal change. Wire shape on the handshake flips one boolean from `false` to
`true`; clients that condition on it gain the correct signal.

### Decision 2 — Rename `ControlCommandKind.Synchronous` → `Sync`

**Problem.** The wire emits `"sync"` and `"job"`; the CLR enum has `Synchronous` and `Job`. The
router maps Synchronous → "sync" on emission
(`src/HotRepl.Core/Control/ControlCommandRouter.cs:303-318`). Two agents tripped on this during the
AK migration alone. The TS client parses defensively (`hotrepl-client.ts` parseDescriptor falls back
to `data.majorVersion ?? data.version` and special-cases `"job"` vs everything else).

**Change.** Rename `ControlCommandKind.Synchronous` to `ControlCommandKind.Sync`. Update all enum
references in core, `HotRepl.UnityCommands`, and the consumer plans. Wire emission is unchanged. The
breaking change is C#-source-only.

**Breaking?** Yes, source-breaking for every handler that names the enum value. Every AK and
Ardenfall handler is touched. Replacement is mechanical (find/replace).

### Decision 3 — Generic-binding `ControlCommandContext<TOutput>` with instance helpers

**Problem.** The current `ControlCommandResult.<Method><TOutput>(...)` static factories require the
explicit `<TOutput>` generic because the compiler cannot infer it from a plain string code/message:

```csharp
return ControlCommandResult.PreconditionFailed<CompendiumExportResult>(
    "dataExporterMissing", "DataExporter mod not found in registered melons.");
```

`ExportJobCommand` repeats this 6 times in 222 lines. Ardenfall wrapped it with
`CompendiumCommandResults.Precondition<T>` (56 lines of pure inference helper).

**Change.** Make the context generic-bound to `TOutput` and host the helpers as instance methods.
The handler signature becomes:

```csharp
public interface IControlCommandHandler<TArgs, TOutput>
{
    string Name { get; }
    int    Version { get; }
    ControlCommandKind Kind { get; }
    bool   MutatesState { get; }

    ValueTask<ControlCommandResult<TOutput>> ExecuteAsync(
        ControlCommandContext<TOutput> context,
        TArgs args,
        CancellationToken cancellationToken);
}

public sealed class ControlCommandContext<TOutput> : ControlCommandContext
{
    public ControlCommandResult<TOutput> Ok(TOutput output);
    public ControlCommandResult<TOutput> Ok(TOutput output, IReadOnlyDictionary<string, ArtifactRef> artifacts);

    public ControlCommandResult<TOutput> PreconditionFailed(string code, string message, object? details = null);
    public ControlCommandResult<TOutput> ValidationFailed(string code, string message, object? details = null);
    public ControlCommandResult<TOutput> Failed(ControlCommandDiagnostic diagnostic);
}
```

Call sites compress:

```csharp
// before
return ControlCommandResult.PreconditionFailed<CompendiumExportResult>("dataExporterMissing", "...");
// after
return context.PreconditionFailed("dataExporterMissing", "...");
```

The non-generic `ControlCommandContext` base remains for users who only need its read-only members
(`RequestId`, `Timeout`, `JobId`, `Progress`, `Artifacts`). The static `ControlCommandResult`
factories are **removed** — there is one way to build a result, on the context.

**Breaking?** Yes. Every handler in core, UnityCommands, AK, and Ardenfall is touched. Replacement
is mechanical (`ControlCommandResult.X<T>(...)` → `context.X(...)`). `ControlCommandResult<TOutput>`
(the return record) is unchanged.

**Why not source generators?** A source generator that synthesizes the four metadata properties from
a `[ControlCommand]` attribute, or one that synthesizes helpers, was considered and rejected.
Analyzer/source-gen distribution is materially worse for BepInEx/MelonLoader HintPath workflows than
for normal NuGet-centered apps. Explicit properties and an explicit context API are stable public
contract, grep-friendly, and trivial to debug. The bug source is API shape, not boilerplate volume;
fix the shape, do not hide it.

### Decision 4 — Optional `[ControlCommand]` attribute for metadata

**Problem.** Four-property metadata blocks at the top of every handler are stable but visually
noisy. We do not want a source generator (see Decision 3 rationale), but a runtime-read attribute is
cheap and lets handlers declare metadata once instead of four times.

**Change.** Add a runtime attribute:

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ControlCommandAttribute : Attribute
{
    public ControlCommandAttribute(string name);
    public string Name { get; }
    public int Version { get; set; } = 1;
    public ControlCommandKind Kind { get; set; } = ControlCommandKind.Sync;
    public bool MutatesState { get; set; }
}
```

`TypedCommandAdapter` looks for the attribute on the handler type. If present, it overrides the
property values (the handler can still define properties — they win for backward-compat if anyone
needs runtime-computed metadata, but the attribute is the recommended path going forward). If
absent, falls back to the properties.

**Breaking?** No. Additive. UnityCommands handlers migrate to the attribute as part of this phase to
validate the path; consumer migrations can adopt at their own pace.

### Decision 5 — `IArtifactWriter` becomes the artifact construction surface

**Problem.** Both real consumers had to write the same file-artifact helper
(`ArtifactCollector.MakeRef` in AK, `CompendiumCommandResults.FileArtifact` in Ardenfall). The
current `InMemoryArtifactWriter` is bytes-only and pessimal for files — `WriteStreamAsync` copies
the entire stream into a `MemoryStream` and then `.ToArray()`s it
(`src/HotRepl.Core/Control/Artifacts/InMemoryArtifactWriter.cs:78-80`). Meanwhile the TS transport
already prefers `ref.path` over `ref.uri` and reads files directly
(`packages/sdk/src/websocket-transport.ts:124-127`), so the protocol-level support is already there
— only the authoring API lags.

**Change.** Expand `IArtifactWriter`:

```csharp
public interface IArtifactWriter
{
    ValueTask<ArtifactRef> AttachBytesAsync(
        string logicalName,
        ReadOnlyMemory<byte> data,
        string contentType,
        CancellationToken cancellationToken = default);

    ValueTask<ArtifactRef> AttachStreamAsync(
        string logicalName,
        Stream stream,
        string contentType,
        CancellationToken cancellationToken = default);

    ValueTask<ArtifactRef> AttachFileAsync(
        string logicalName,
        string path,
        string contentType,
        CancellationToken cancellationToken = default);
}
```

- `AttachBytesAsync` is the bytes path (replaces `WriteAsync(bytes)` with the same semantics, less
  allocation).
- `AttachStreamAsync` streams through `IncrementalHash.SHA256` while writing to backing storage. No
  `ToArray`.
- `AttachFileAsync` reads from disk, hashes incrementally, stamps `uri = file://...`,
  `path = absolute`, `byteSize = FileInfo.Length`, `finalized = true`. Does **not** copy the file —
  the artifact references the existing path. Consumers that need a copy do it themselves before
  attaching.

`InMemoryArtifactWriter` retains in-memory semantics for tests but its `WriteStreamAsync` hot path
stops the buffer copy. A second implementation, `FileSystemArtifactWriter`, is considered
out-of-scope for this phase (no real consumer needs a copy-aware writer; both existing consumers
reference files at their natural location).

**Truthful `ArtifactsSchema`.** Today's `TypedCommandAdapter` hard-codes
`artifactsSchema: SchemaCache.AnyObject`
(`src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs:33-41`). The descriptor exposes an
`artifactsSchema` field on the wire but it carries no info.

Make it real by allowing handlers to declare expected artifacts via a per-command attribute or a
`static IReadOnlyDictionary<string, ArtifactSpec> ArtifactKeys { get; }` convention. The shape:

```csharp
[ControlCommandArtifact("data.<stem>",          ContentType = "application/json", Required = true,  RepeatCount = "1..*")]
[ControlCommandArtifact("visual-assets.manifest", ContentType = "application/json", Required = true)]
[ControlCommandArtifact("screenshots.metadata",   ContentType = "application/json", Required = false)]
[ControlCommandArtifact("screenshots.<stem>",     ContentType = "image/png",        Required = false, RepeatCount = "0..*")]
public sealed class ExportJobCommand : IControlCommandHandler<...> { ... }
```

The adapter compiles these into a real JSON Schema (a `patternProperties` shape) that surfaces to
MCP clients via `command_describe`. Authors that don't care can omit the attribute and get the
current `{ "type": "object" }` open shape.

**Breaking?** Yes — `IArtifactWriter` signature changes. The only current implementation is in-tree.
Consumer impact: AK's `ArtifactCollector.Collect(exportDir, screenshotDir,
includeScreenshots)`
collapses to ~10 `context.Artifacts.AttachFileAsync(...)` calls in `ExportJobCommand`. Ardenfall's
`FileArtifact` helper is deleted; handlers call `context.Artifacts.AttachFileAsync(...)` directly.

### Decision 6 — Ship first-party `HotRepl.Sdk` (netstandard2.0)

**Problem.** The most expensive duplication in the entire migration. AK rebuilt the TS SDK's
`Session` from scratch in C# (`HotReplExportRunner.cs` + `IHotReplTransport.cs` +
`ClientWebSocketTransport.cs` = 344 LOC). No C# consumer can use the protocol cleanly.

**Change.** Ship a new `HotRepl.Sdk` package. netstandard2.0 (broadest compatibility: BepInEx Mono,
MelonLoader IL2CPP build tools, .NET 6/8/10, .NET Framework 4.6.1+). API shape was specified via a
survey of Azure, OpenAI, Octokit, Discord.Net, StreamJsonRpc:

```csharp
namespace HotRepl.Sdk;

public sealed class HotReplClient
{
    public HotReplClient(Uri endpoint, HotReplClientOptions? options = null);
    public Task<HotReplSession> ConnectAsync(CancellationToken cancellationToken = default);
}

public sealed class HotReplClientOptions
{
    public TimeSpan ConnectTimeout       { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan RequestTimeout       { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan JobPollingInterval   { get; set; } = TimeSpan.FromMilliseconds(250);
    public bool     ValidateSchemas      { get; set; }  // default false; opt-in client-side
    public JsonSerializerSettings? SerializerSettings { get; set; }
}

public sealed class HotReplSession : IAsyncDisposable
{
    public HotReplCapabilities Capabilities { get; }
    public bool                IsConnected   { get; }

    public Task<IReadOnlyList<CommandSummary>> ListCommandsAsync(CancellationToken ct = default);
    public Task<CommandDescriptor>            DescribeCommandAsync(string name, CancellationToken ct = default);

    // sync commands
    public Task<HotReplResult<TResult>> RunAsync<TArgs, TResult>(
        string command,
        TArgs args,
        HotReplRunOptions? options = null,
        CancellationToken cancellationToken = default);

    public Task<HotReplResult<TResult>> RunAsync<TResult>(
        string command,
        IReadOnlyDictionary<string, object?> args,
        HotReplRunOptions? options = null,
        CancellationToken cancellationToken = default);

    public Task<HotReplResult<JToken>> RunRawAsync(
        string command,
        JToken args,
        HotReplRunOptions? options = null,
        CancellationToken cancellationToken = default);

    // jobs
    public Task<HotReplJob<TResult>> StartJobAsync<TArgs, TResult>(
        string command,
        TArgs args,
        HotReplRunOptions? options = null,
        CancellationToken cancellationToken = default);

    // eval/complete/subscribe — full parity with TS SDK
    public Task<EvalResponse<T>>       EvalAsync<T>(string code, TimeSpan? timeout = null, CancellationToken ct = default);
    public Task<IReadOnlyList<string>> CompleteAsync(string code, int? cursor = null, CancellationToken ct = default);
    public Task                        ResetAsync(CancellationToken ct = default);
    public IAsyncEnumerable<WatchTick<T>> WatchAsync<T>(string code, CancellationToken ct = default);

    public Task CloseAsync(CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();
}

public sealed class HotReplJob<TResult>
{
    public string             Id              { get; }
    public HotReplJobStatus   LastKnownStatus { get; }
    public IAsyncEnumerable<HotReplJobProgress> Progress { get; }

    public Task<HotReplJobStatus>       GetStatusAsync(CancellationToken ct = default);
    public Task                         CancelAsync(CancellationToken ct = default);
    public Task<HotReplResult<TResult>> WaitForCompletionAsync(CancellationToken ct = default);
    public Task<HotReplResult<TResult>> WaitForCompletionAsync(TimeSpan pollingInterval, CancellationToken ct);
}

public sealed class HotReplResult<TResult>
{
    public TResult                                Output    { get; }
    public IReadOnlyDictionary<string, Artifact>  Artifacts { get; }
    // Diagnostics are surfaced via exceptions on failure paths.
}

public sealed class Artifact
{
    public ArtifactRef Ref { get; }
    public Task<byte[]>  BytesAsync(CancellationToken ct = default);
    public Task<string>  TextAsync(Encoding? encoding = null, CancellationToken ct = default);
    public Task<T>       JsonAsync<T>(CancellationToken ct = default);
}

public class HotReplException : Exception
{
    public string             Code        { get; }
    public HotReplErrorKind   Kind        { get; }
    public bool               Retryable   { get; }
    public JToken?            Details     { get; }
}
public sealed class HotReplConnectionException : HotReplException { ... }
public sealed class HotReplProtocolException   : HotReplException { ... }
public sealed class HotReplCommandException    : HotReplException { ... }
public sealed class HotReplJobFailedException  : HotReplException { ... }
public sealed class HotReplSessionEvictedException : HotReplException { ... }
```

Conventions (every one source-verified against the SDK survey, see `agent://52-CSharpSdkPatterns`):

1. **Constructor is configuration, `ConnectAsync` is the live boundary.** No I/O in the constructor.
   Matches Discord.Net `StartAsync`, StreamJsonRpc `Attach`+`StartListening`, websocket-client
   `Start`.
2. **Request/response correlation:** `ConcurrentDictionary<string, TaskCompletionSource<JObject>>`
   keyed by protocol `id`. Direct copy of StreamJsonRpc's `resultDispatcherMap`.
3. **Trailing `CancellationToken ct = default` on every async method.**
4. **Timeouts in options, not as CT-only.** Internally compose linked CTs.
5. **Reconnect is opt-out by default.** HotRepl's single-client/session-eviction policy makes silent
   reconnect unsafe — pending requests would surface stale responses across a session that no longer
   owns them. Reconnect can be added later as opt-in `HotReplReconnectPolicy` if a real consumer
   needs it.
6. **Exceptions for failure paths, not `Result<T, E>`.** No surveyed C# SDK uses `Result<T, E>` as
   its primary error model. Custom hierarchy with `Code`, `Kind`, `Retryable`, `Details` mirrors the
   TS `HotReplError` shape.
7. **Jobs use `WaitForCompletionAsync` + `IAsyncEnumerable<HotReplJobProgress> Progress`.** The
   `IAsyncEnumerable` is consumed in parallel with awaiting completion. Modeled on Azure
   `Operation<T>.WaitForCompletionAsync` + OpenAI `AsyncCollectionResult<StreamingUpdate>`.

**Newtonsoft.Json or System.Text.Json?** Newtonsoft. HotRepl.Core is Newtonsoft; HotRepl.Protocol
message records use Newtonsoft. Making the SDK STJ would force a serializer boundary at every API
call. Newtonsoft is also already present in every BepInEx install via `BepInEx.Core` (and ships
transitively wherever HotRepl.Core deploys).

**Newtonsoft.Json.Schema?** No. Schema validation on the client side is opt-in via the
`ValidateSchemas` option and uses NJsonSchema (same as the server) only if the option is enabled.
Default behavior: skip client-side validation, let the server enforce it. This keeps the SDK's
mandatory dependency surface small.

### Decision 7 — Ship `HotRepl.Testing` (netstandard2.0)

**Problem.** Both real consumers wrote essentially identical test plumbing — a
`HandlerHarness`-shaped utility, a schema-snapshot assertion, an artifact-collector stability test.
Ardenfall's `TypedCommandRegistryTests.cs` is 112 lines doing the same thing as AK's
`CommandCatalogTests.cs` + `DtoSchemaTests.cs` + `ArtifactCollectorTests.cs`.

**Change.** Ship a test-helper package on netstandard2.0:

```csharp
namespace HotRepl.Testing;

public static class HandlerHarness
{
    // Schema-side
    public static JObject     GenerateSchema<T>();
    public static SchemaValidationResult Validate<TArgs>(string json);
    public static SchemaValidationResult Validate<TArgs>(JObject args);

    // Execution-side (in-process, no transport)
    public static Task<HandlerResult<TOutput>> RunAsync<TArgs, TOutput>(
        IControlCommandHandler<TArgs, TOutput> handler,
        TArgs args,
        IArtifactWriter? artifactWriter = null,
        CancellationToken cancellationToken = default);

    // Catalog-side
    public static ControlCommandDescriptor DescribeHandler<TArgs, TOutput>(IControlCommandHandler<TArgs, TOutput> handler);
}

public sealed class HandlerResult<TOutput>
{
    public bool                                   Succeeded   { get; }
    public TOutput?                               Output      { get; }
    public IReadOnlyDictionary<string, ArtifactRef> Artifacts { get; }
    public IReadOnlyList<ControlCommandDiagnostic> Diagnostics { get; }
}

public static class ConformanceSuite
{
    public static Task RunAllAsync(HotReplSession session, ConformanceOptions? options = null, CancellationToken ct = default);
}
```

The conformance suite is the C# sibling of `packages/conformance/src/index.ts`. Both suites validate
the same wire-protocol behaviours against the same running server. If they diverge, one or both is
wrong.

**Why a separate package, not inside `HotRepl.Core`?** xUnit (or any test framework) is a test-only
dependency. Bundling test helpers into the core dll would pull test surface into every loaded mod.

### Decision 8 — Cache `commands_list` per session in both SDKs

**Problem.** `Session.run()` in the TS SDK calls `describeCommand()` on first use of every command
(`packages/sdk/src/session.ts:151-152`), just to learn `kind` so it can decide sync-vs-job dispatch.
But `commands_list` already carries `kind` and `majorVersion` on every entry
(`packages/protocol/src/messages.ts:53-80`). The extra round-trip is unnecessary.

**Change.** Both SDKs (TS and the new C# SDK) lazily fetch `commands_list` on first use and cache
the result. `Run`/`RunAsync` consult the catalog for `kind`. `command_describe` is only called when
the caller explicitly asks for the schema via `describeCommandAsync(name)` or when a future
"validate args before sending" path needs the schema.

**Breaking?** Yes for any TS consumer that depended on the implicit `command_describe` side-effect
(i.e. populating the descriptor cache as a side-effect of `run`). Such a consumer would have to call
`describeCommandAsync` explicitly. Survey of in-repo consumers: only `@hotrepl/mcp` reads
descriptors, and it does so explicitly via `session.request({ type: "commands_list" })`
(`packages/mcp/src/tools.ts:140-145`).

### Decision 9 — Promote UnityCommands to canonical-sample status

**Problem.** Future plugin authors copy from AK or Ardenfall because there is no blessed reference
outside them. AK and Ardenfall carry domain code that confuses the example.
`HotRepl.UnityCommands.BepInEx` and `HotRepl.UnityCommands.MelonLoader` already exist (4 commands
each, both loader variants); they're shipped but undocumented as samples.

**Change.** Author `docs/authoring-commands.md`:

- One-page quick start, BepInEx + MelonLoader side-by-side.
- DTO conventions, `[ControlCommand]` attribute, context helpers.
- File-artifact pattern with `context.Artifacts.AttachFileAsync(...)`.
- Job pattern (start, progress, terminal).
- Testing pattern with `HandlerHarness`.
- Reference: "see `src/HotRepl.UnityCommands/Commands/` for working examples."

Update `README.md` and `AGENTS.md` to point to it. Update
`src/HotRepl.UnityCommands.BepInEx/README.md` and the MelonLoader sibling to say "this is the
canonical sample mod; new authors should fork from here."

Nothing in `samples/` directory tree — the UnityCommands packages are the samples.

## API breaks across the consumer surface

After Phase 4, the public C# authoring surface looks like this:

```csharp
[ControlCommand("compendium.export", Version = 1, Kind = ControlCommandKind.Job, MutatesState = true)]
public sealed class ExportJobCommand : IControlCommandHandler<CompendiumExportArgs, CompendiumExportResult>
{
    private readonly DataExporter _dataExporter;
    private readonly MapScreenshotter _screenshotter;

    public ExportJobCommand(DataExporter dataExporter, MapScreenshotter screenshotter)
    {
        _dataExporter = dataExporter;
        _screenshotter = screenshotter;
    }

    public async ValueTask<ControlCommandResult<CompendiumExportResult>> ExecuteAsync(
        ControlCommandContext<CompendiumExportResult> context,
        CompendiumExportArgs args,
        CancellationToken cancellationToken)
    {
        context.Progress.Report("enteringWorld", "Checking world readiness.");

        if (NetworkClient.localPlayer == null)
        {
            var entered = await EnterWorldAsync(cancellationToken);
            if (!entered.Ok) return context.PreconditionFailed(entered.Code, entered.Message);
        }

        context.Progress.Report("exportingData", "Running DataExporter.ExportAllData().");
        var exported = _dataExporter.ExportAllData();
        if (!exported.Ok)
            return context.PreconditionFailed("dataExportFailed",
                $"DataExporter reported {exported.Errors.Count} error(s).");

        // Artifacts attach through the writer; SHA-256, byteSize, uri, finalized all stamped here.
        foreach (var file in exported.OutputFiles)
            await context.Artifacts.AttachFileAsync(
                logicalName: $"data.{file.Stem}",
                path: file.AbsolutePath,
                contentType: "application/json",
                cancellationToken);

        if (args.Screenshots)
        {
            context.Progress.Report("capturingScreenshots", "Starting MapScreenshotter.");
            // ... screenshot capture loop ...
            await context.Artifacts.AttachFileAsync("screenshots.metadata", metaPath, "application/json", cancellationToken);
            foreach (var png in screenshotter.LastResult.Files)
                await context.Artifacts.AttachFileAsync($"screenshots.{png.Stem}", png.Path, "image/png", cancellationToken);
        }

        return context.Ok(new CompendiumExportResult
        {
            Ok = true,
            DurationMs = ...,
            ExporterCount = exported.Exporters.Count,
            ScreenshotCount = ...,
        });
    }
}
```

Compared to the v3 shape (`ExportJobCommand.cs` 222 lines, AK):

- 4 lines of metadata properties → 1 attribute line.
- 6 `PreconditionFailed<CompendiumExportResult>(...)` → 6 `context.PreconditionFailed(...)`.
- ~30 lines of `ArtifactCollector.Collect` + `MakeRef` helper → 10 inline
  `context.Artifacts.AttachFileAsync` calls.
- Synchronous nullable-disabled `WorldEntryOutcome` wrapper unchanged.
- `Progress` snapshot construction noise → `context.Progress.Report(phase, message)`.

Estimated handler shrinkage: ~30%.

## Wire protocol — what changes, what doesn't

**Stable** (no wire change):

- Handshake shape (`protocolVersion: 2`).
- Message types (`commands_list`, `command_describe`, `command_call`, `job_accepted`, `job_status`,
  `job_status_result`, `job_result`, `job_cancel`, etc.).
- `ArtifactRef` wire shape (`logicalName`, `uri`, `path?`, `contentType`, `byteSize`, `sha256`,
  `finalized`).
- `CommandDescriptor` wire shape (`name`, `version`, `kind`, `mutatesState`, schemas).
- `kind` continues to emit as `"sync"`/`"job"` strings.

**Cleaner** (no client-visible break):

- `schemaValidation` in the handshake reports `true` instead of `false` (it was lying before).
  Clients that already condition on it will start seeing accurate values.
- `artifactsSchema` on descriptors becomes meaningful when handlers declare expected keys; remains
  `{ "type": "object" }` when they don't. Clients that ignored it continue to work; MCP clients that
  read it gain useful information.

**Source-only break** (clients on the C# side update; no wire change):

- C# enum rename `Synchronous` → `Sync`.
- C# context-generic on handler signature.
- C# `IArtifactWriter` signature.

## Verification

### Unit and integration tests

- Every new API surface (`ControlCommandContext<TOutput>`, `[ControlCommand]` attribute,
  `IArtifactWriter` methods, `ControlCommandKind.Sync`, validator caching) lands with xUnit coverage
  under `tests/HotRepl.Tests/`.
- `HotRepl.Sdk` lands with xUnit coverage under `tests/HotRepl.Sdk.Tests/`. Uses a fake-transport
  double matching how `tests/HotRepl.Tests/Integration/TypedCommandRoundTripTests.cs` exercises the
  existing typed-command surface.
- `HotRepl.Testing` lands with xUnit coverage under `tests/HotRepl.Testing.Tests/`. Validates
  `HandlerHarness` against the in-tree `UnityCommands` handlers (which become test fixtures by
  virtue of being canonical samples).

### Cross-SDK conformance

The new C# `ConformanceSuite` and the existing TS `@hotrepl/conformance` package run against the
same running server (a `FakeRuntime` in tests, real HotRepl on the wire in opt-in scenarios) and
must produce identical pass/fail sets. CI runs both in `pre-push`.

### Consumer build smoke tests

Before Phase 4 is marked done:

- `ardenfall-compendium` mod project builds against the new `HotRepl.Core.dll` with **no code
  changes other than the mechanical renames** required by Decisions 2 and 3. Any semantic break is a
  Phase 4 bug to fix, not a consumer migration.
- `ancient-kingdoms-mods` HotReplCommands project builds the same way.

The two consumer-migration plans (Phase 4b Ardenfall, Phase 4c AK) then land separately and adopt
the new APIs (context helpers, file-artifact attach, SDK in build-tool).

### Live game validation

Phase 4 itself does not block on live game validation; the AK + Ardenfall consumer plans own that
gate.

## Out of scope

| Item                                                 | Why                                                                                                                      |
| ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| Re-baseline wire to JSON-RPC 2.0 / StreamJsonRpc     | Dependency sprawl, IL2CPP-hostile dynamic proxies, no schema contract, jobs aren't native. Solves the wrong layer.       |
| Replace NJsonSchema with `JsonSchema.Net.Generation` | STJ-centric, ignores Newtonsoft attrs, ships under OSMF EULA. Validator caching fixes the actual cost.                   |
| Source generator for handler metadata                | Hostile to BepInEx/MelonLoader HintPath workflows; masks an API design problem with tooling.                             |
| `Result<T, E>` error model in the C# SDK             | No surveyed mature C# SDK uses it; exceptions remain the dominant idiom and integrate with `await` / framework handlers. |
| Built-in automatic reconnect in the SDK              | HotRepl's single-client/session-eviction model makes silent reconnect unsafe. Opt-in policy can be added later.          |
| `FileSystemArtifactWriter` (copy-aware)              | No real consumer needs a copy; both existing consumers reference files at their natural location. Add when needed.       |
| Hot-reloading the command registry                   | Mid-flight registry mutation is hard to get right. Restart-required is fine.                                             |
| C# CLI as primary .NET integration story             | A CLI is a thin wrapper on top of the SDK. Shell-out is not a substitute for a library in build-tools/tests.             |

## Acceptance criteria

Phase 4 is complete when:

- [ ] `HotRepl.Core` builds clean on the new authoring API (Decisions 1–5) with all existing
      `tests/HotRepl.Tests/` passing plus new coverage for each decision.
- [ ] `HotRepl.UnityCommands` handlers migrated to `[ControlCommand]` attribute,
      `ControlCommandContext<TOutput>` helpers, and `context.Artifacts.AttachFileAsync` (where they
      emit artifacts). All BepInEx and MelonLoader variants build clean.
- [ ] `HotRepl.Sdk` package builds clean (netstandard2.0), produces a nuget package via
      `dotnet pack`, and has xUnit coverage with a fake transport double.
- [ ] `HotRepl.Testing` package builds clean (netstandard2.0), produces a nuget package, and has
      xUnit coverage demonstrating `HandlerHarness` against the UnityCommands sample handlers.
- [ ] The C# `ConformanceSuite` and the TS `@hotrepl/conformance` pass against the same running
      server with the same set of cases.
- [ ] Both SDKs cache `commands_list` per session; only call `command_describe` when schema is
      explicitly requested.
- [ ] `docs/authoring-commands.md` exists, walks through both runtimes, and is referenced from
      `README.md` and `AGENTS.md`.
- [ ] `lefthook run pre-push --force` clean.
- [ ] **Ardenfall** and **Ancient Kingdoms** mod projects build clean against the new
      `HotRepl.Core.dll` after only the mechanical renames required by Decisions 2 and 3. (Their own
      deeper migrations — drop `hotrepl-client.ts`, drop `HotReplExportRunner.cs`, adopt
      `HotRepl.Sdk`, adopt `context.Artifacts.AttachFileAsync` — ship in Phase 4b/4c.)

## Open questions

- **Should `[ControlCommandArtifact]` declarations be expressed as a static `ArtifactKeys`
  dictionary instead of attributes?** Both surface to the descriptor the same way. Attribute is more
  local; dictionary is more dynamic-friendly (computed keys for plugins with dynamic catalogs).
  Decide during implementation; if neither real consumer needs computed keys, attribute wins.
- **Should `HotRepl.Sdk` expose `RunAsync<TArgs, TResult>` as the only typed entry, or also
  `RunAsync<TResult>(string command, object args)` for callers without a shared DTO assembly?**
  Survey says both. Lean toward both. Decide based on AK build-tool call-site shape (the only known
  C# consumer for the SDK at Phase 4 launch).
- **Reconnect policy.** Opt-in. Should we ship a stub `HotReplReconnectPolicy.None` / `Manual` enum
  surface in Phase 4 so the option exists, or leave it out entirely until a consumer needs it? Lean
  toward leaving out.
- **`HotRepl.Testing.ConformanceSuite` test discovery.** xUnit theory-based, or `IClassFixture` +
  `[Fact]` per case? Whichever matches the existing `HotRepl.Tests` style most cleanly.

## Phase 4 — deliverables and follow-ups

This spec covers Phase 4a (HotRepl-internal). Two follow-on specs and plans land afterwards:

- **Phase 4b — Ardenfall update.** Drop `controller/src/hotrepl-client.ts`. Drop
  `mod/src/Control/CompendiumCommandResults.cs`. Migrate handlers to
  `ControlCommandContext<TOutput>` helpers and `context.Artifacts.AttachFileAsync`. Adopt the
  `[ControlCommand]` attribute (optional but recommended). Spec written when Phase 4a lands.

- **Phase 4c — Ancient Kingdoms update.** Delete
  `build-tool/HotRepl/{HotReplExportRunner,
  IHotReplTransport, ClientWebSocketTransport}.cs` and
  replace with `HotRepl.Sdk`-driven invocation. Migrate `HotReplCommands` mod handlers to the new
  context API. Migrate `ArtifactCollector` callers to `context.Artifacts.AttachFileAsync`. Spec
  written when Phase 4a lands.

After Phase 4 (a/b/c) is done, the existing Phase 4 (Erenshor) becomes Phase 5, and Phase 5
(Documentation) becomes Phase 6. The roadmap doc is updated to reflect this.
