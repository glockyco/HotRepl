# Phase 1 — typed-commands foundation

Detailed design for the first phase of the typed-commands roadmap
([`2026-05-24-typed-commands-roadmap.md`](2026-05-24-typed-commands-roadmap.md)).

Phase 1 ships:

- `HotRepl.Core` 3.0.0, with the new strongly-typed command interface replacing the v2
  `IControlCommandHandler`.
- `HotRepl.UnityCommands` — a first-party demo plugin with 4 commands for both BepInEx/Mono and
  MelonLoader/IL2CPP.
- `glockyco/hotrepl-mod-template` — a GitHub template repo (with embedded `dotnet new` manifest) for
  one-click mod scaffolding.

It does **not** ship any consumer migrations. Those are Phases 2–4. But Phase 1's API is designed
against the constraints those migrations will impose; see "Workloads the API must support" below.

---

## 1. Goal

Replace HotRepl's v2 command-authoring pattern (write a handler against `IControlCommandHandler`,
hand-write the `JObject` argument schema, hope the schema matches the deserialization code) with v3:
implement `IControlCommandHandler<TArgs, TOutput>` against typed POCO arg and result types; schemas,
validation, and the protocol-level dispatch shape are all derived automatically.

The new shape must support every existing use case (sync read commands, sync mutating commands,
long-running jobs with progress, multi-named artifact outputs, validation failures with structured
diagnostics), be ergonomic enough that a new plugin author can copy a demo command and have
something working, and ship through the existing `HotRepl.{BepInEx,Host.MelonLoader}` host plugins
without changing the consumer deploy-script contract.

## 2. Workloads the API must support

These are the patterns Phases 2–4 will exercise. Each is locked as a hard requirement on the Phase 1
API design.

| Pattern                                | Example                                                | What it requires                                                                                                                                                                                                              |
| -------------------------------------- | ------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **No-args sync read**                  | `unity.app.info`                                       | `TArgs == EmptyArgs`; sync return of `TOutput`; no artifacts; no diagnostics.                                                                                                                                                 |
| **Typed-args sync read**               | `unity.gameobject.find`                                | `TArgs` with `[Required]`/`[Range]`/`[Description]` annotations; nullable `TOutput`.                                                                                                                                          |
| **Mutating sync**                      | `unity.time.set_scale`                                 | `MutatesState = true` (flows into MCP `destructiveHint` annotation); `[Range]` on arg validated server-side.                                                                                                                  |
| **Artifact-producing job**             | `unity.screenshot.capture`                             | Result wrapper carries top-level `Artifacts` dict; handler writes a PNG and gets back an `ArtifactRef`; command is a job because Unity frame capture must wait for `WaitForEndOfFrame` without blocking `Tick()`.             |
| **Job with progress**                  | (Phase 3) `ak.export.run`                              | `ControlCommandKind.Job`; handler uses `context.Progress.Report(...)` during execution; `CancellationToken` honored.                                                                                                          |
| **Job producing many named artifacts** | (Phase 3) `ak.export.run`                              | Result wrapper's `Artifacts` dict carries multiple named refs (manifest, items, asset-manifest, etc.).                                                                                                                        |
| **Multi-frame Unity workflow**         | `unity.screenshot.capture`; (Phase 3) `ak.world.enter` | Screenshot uses a loader-provided coroutine bridge; plain `async/await` still works for non-coroutine workflows because `UnitySynchronizationContext` posts continuations to the next frame on the main thread.               |
| **Structured failure**                 | (Phase 2) Ardenfall's `RunBeginCommand` with bad slug  | Handler returns `ControlCommandResult.Failed(...)` with a `ControlCommandDiagnostic` of `Kind = validation_failed` or `precondition_failed`; wire `status = "failed"`; client receives diagnostic without an exception trace. |
| **Long-running streaming progress**    | (Phase 4) `erenshor.map.capture_chunks`                | Same job-with-progress pattern; each `Progress.Report(...)` becomes a `subscribe_result`-style event the client can consume.                                                                                                  |

The interface designed below covers each row without requiring per-pattern extensions.

## 3. Public API

### 3.1 Handler interface

```csharp
namespace HotRepl.Control;

/// <summary>
/// Authoring-time interface for a typed control-plane command handler.
/// </summary>
/// <typeparam name="TArgs">
///     POCO argument type. Use <see cref="EmptyArgs"/> for commands that
///     take no arguments. Properties decorated with <c>[Required]</c>,
///     <c>[Range]</c>, <c>[Description]</c>, <c>[JsonProperty]</c> etc.
///     surface in the generated schema and are validated server-side
///     before the handler runs.
/// </typeparam>
/// <typeparam name="TOutput">
///     POCO output type. The same attribute rules apply; the schema
///     surfaces to clients via <c>command_describe</c>.
/// </typeparam>
public interface IControlCommandHandler<TArgs, TOutput>
{
    /// <summary>Stable wire name (e.g. <c>"compendium.info"</c>).</summary>
    string Name { get; }

    /// <summary>
    ///     Wire-protocol major version. Bump when the args or output
    ///     shape changes incompatibly.
    /// </summary>
    int Version { get; }

    /// <summary>Synchronous (in-band response) or Job (out-of-band).</summary>
    ControlCommandKind Kind { get; }

    /// <summary>
    ///     True if this command may change game/runtime state. Used by MCP
    ///     to set the <c>destructiveHint</c> tool annotation.
    /// </summary>
    bool MutatesState { get; }

    /// <summary>
    ///     Execute the command. Invoked on the host's main-thread execution
    ///     path (via <c>ReplEngine.Tick()</c>). Continuations after
    ///     <c>await</c> resume on the Unity main thread via
    ///     <see cref="System.Threading.SynchronizationContext"/>.
    /// </summary>
    ValueTask<ControlCommandResult<TOutput>> ExecuteAsync(
        ControlCommandContext context,
        TArgs args,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Marker type for handlers that take no arguments. The schema generator
/// emits <c>{ "type": "object", "additionalProperties": false }</c>.
/// </summary>
public readonly struct EmptyArgs
{
}
```

The previous non-generic `IControlCommandHandler` is **deleted**. There is exactly one public way to
author a command in v3.

### 3.2 Result wrapper

```csharp
namespace HotRepl.Control;

/// <summary>
/// Result returned by a typed command handler.
/// </summary>
/// <remarks>
///     Wire shape after adaptation: a <c>command_result</c> /
///     <c>job_result</c> message with <c>output</c>,
///     <c>artifacts</c> (named map), and <c>diagnostics</c> at the top
///     level. The <see cref="Output"/> POCO is serialized to JObject
///     for the wire; <see cref="Artifacts"/> and
///     <see cref="Diagnostics"/> are passed through to the existing
///     wire-level fields unchanged.
/// </remarks>
public sealed class ControlCommandResult<TOutput>
{
    private static readonly IReadOnlyDictionary<string, ArtifactRef> EmptyArtifacts =
        new Dictionary<string, ArtifactRef>(0, StringComparer.Ordinal);
    /// <summary>Successful output. Null for failed results.</summary>
    public TOutput? Output { get; init; }

    /// <summary>Named artifact refs. Key is the logical name.</summary>
    public IReadOnlyDictionary<string, ArtifactRef> Artifacts { get; init; }
        = EmptyArtifacts;

    /// <summary>Diagnostics (warnings, validation failures, etc.).</summary>
    public IReadOnlyList<ControlCommandDiagnostic> Diagnostics { get; init; }
        = Array.Empty<ControlCommandDiagnostic>();

    /// <summary>
    ///     True if the command succeeded. Drives wire <c>status</c>
    ///     ("ok" vs "failed"). When false, <see cref="Output"/> is
    ///     typically null and at least one diagnostic explains why.
    /// </summary>
    public bool Succeeded { get; init; } = true;

}

/// <summary>Non-generic factory helpers for typed command results.</summary>
public static class ControlCommandResult
{
    public static ControlCommandResult<TOutput> Ok<TOutput>(TOutput output)
        => new() { Output = output };

    public static ControlCommandResult<TOutput> Ok<TOutput>(
        TOutput output,
        string artifactName,
        ArtifactRef artifact)
        => new()
        {
            Output = output,
            Artifacts = new Dictionary<string, ArtifactRef>(StringComparer.Ordinal)
                { [artifactName] = artifact },
        };

    public static ControlCommandResult<TOutput> Ok<TOutput>(
        TOutput output,
        IReadOnlyDictionary<string, ArtifactRef> artifacts)
        => new() { Output = output, Artifacts = artifacts };

    public static ControlCommandResult<TOutput> ValidationFailed<TOutput>(
        string code, string message, object? details = null)
        => Failed<TOutput>(new ControlCommandDiagnostic(
            ControlCommandDiagnosticKind.ValidationFailed, code, message, retryable: false, details));

    public static ControlCommandResult<TOutput> PreconditionFailed<TOutput>(
        string code, string message, object? details = null)
        => Failed<TOutput>(new ControlCommandDiagnostic(
            ControlCommandDiagnosticKind.PreconditionFailed, code, message, retryable: false, details));

    public static ControlCommandResult<TOutput> Failed<TOutput>(ControlCommandDiagnostic diagnostic)
        => new()
        {
            Succeeded = false,
            Diagnostics = new[] { diagnostic },
        };
}

/// <summary>
/// Diagnostic carried in a command result. Failure diagnostics drive
/// the wire <c>status = "failed"</c> shape; informational diagnostics
/// ride alongside a successful result.
/// </summary>
public sealed record ControlCommandDiagnostic(
    ControlCommandDiagnosticKind Kind,
    string Code,
    string Message,
    bool Retryable = false,
    object? Details = null
);

public enum ControlCommandDiagnosticKind
{
    Info,
    Warning,
    ValidationFailed,
    PreconditionFailed,
    Conflict,
    Cancelled,
}
```

### 3.3 Execution context

```csharp
namespace HotRepl.Control;

/// <summary>
/// Per-invocation context handed to a command handler.
/// </summary>
public sealed class ControlCommandContext
{
    /// <summary>
    ///     Originating wire request ID. Use for log correlation; do not
    ///     parse for semantic content.
    /// </summary>
    public string RequestId { get; }

    /// <summary>
    ///     Caller-requested timeout. Null means no explicit caller
    ///     timeout (server-side limits still apply).
    /// </summary>
    public TimeSpan? Timeout { get; }

    /// <summary>
    ///     Job ID, if this command is a job. Null for synchronous
    ///     commands.
    /// </summary>
    public string? JobId { get; }

    /// <summary>
    ///     Progress sink. For synchronous commands, calls to
    ///     <see cref="IProgress{T}.Report"/> are silently dropped.
    ///     For job commands, each Report becomes a
    ///     <c>job_status_result</c> snapshot visible to polling
    ///     clients and a <c>subscribe_result</c>-style event in the
    ///     job event buffer.
    /// </summary>
    public IProgress<ControlCommandProgress> Progress { get; }

    /// <summary>
    ///     Artifact writer. Handlers call
    ///     <see cref="IArtifactWriter.WriteAsync"/> to materialize a
    ///     binary blob; the returned ref goes into the result
    ///     wrapper's <see cref="ControlCommandResult{T}.Artifacts"/>
    ///     dict. The writer is shared across the handler's lifetime;
    ///     calls are idempotent on logical name (a second write with
    ///     the same name replaces the first).
    /// </summary>
    public IArtifactWriter Artifacts { get; }

    // …constructor + internal hookup omitted for spec brevity…
}

/// <summary>Progress payload for a job command.</summary>
public sealed record ControlCommandProgress(
    /// <summary>Optional structured progress (e.g. percentages, counts).</summary>
    JObject? Snapshot = null,
    /// <summary>Optional human-readable message (e.g. "exporting items").</summary>
    string? Message = null
);

/// <summary>
/// Writes binary artifacts that a command produces. Implementations
/// live in <c>HotRepl.Core</c> and persist into the host's configured
/// artifact store (typically a temp directory enumerated under the
/// game's plugin folder; see <c>ReplConfig.ArtifactDirectory</c>).
/// </summary>
public interface IArtifactWriter
{
    ValueTask<ArtifactRef> WriteAsync(
        string logicalName,
        ReadOnlyMemory<byte> bytes,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    );

    ValueTask<ArtifactRef> WriteStreamAsync(
        string logicalName,
        Stream stream,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    );
}
```

### 3.4 Registry

```csharp
namespace HotRepl.Control;

/// <summary>
/// Process-wide registry of control-plane command handlers.
/// </summary>
public interface IControlCommandRegistry
{
    /// <summary>Descriptors advertised to clients.</summary>
    IReadOnlyList<ControlCommandDescriptor> Describe();

    /// <summary>
    ///     Register a typed command. The returned disposable
    ///     unregisters on dispose; use it for proper teardown in
    ///     plugin <c>OnDestroy</c>/<c>OnDeinitializeMelon</c>.
    /// </summary>
    IDisposable Register<TArgs, TOutput>(IControlCommandHandler<TArgs, TOutput> handler);
}
```

`ICompiledControlCommand` is the internal dispatch shape (effectively today's v2
`IControlCommandHandler`). It stays `internal`/non-public so the only consumer-facing handler
interface is the typed one. Dispatch lookup lives behind an internal paired interface:

```csharp
namespace HotRepl.Control.Internal;

internal interface ICompiledRegistry
{
    bool TryGet(string name, out ICompiledControlCommand? handler);
}
```

`GlobalControlCommandRegistry.Instance` is the canonical singleton (no change from v2). Its
`Register(IControlCommandHandler<,>)` instantiates the internal adapter (§4.1) and stores the
resulting `ICompiledControlCommand`; concrete registries implement `ICompiledRegistry` explicitly so
`TryGet` does not appear on the public API.

## 4. Internals

### 4.1 The typed → compiled adapter

Internal to `HotRepl.Core`. Consumers never reference it.

```csharp
namespace HotRepl.Control;

/// <summary>Internal dispatch shape consumed by the router.</summary>
internal interface ICompiledControlCommand
{
    ControlCommandDescriptor Descriptor { get; }

    ValueTask<CompiledCommandResult> ExecuteAsync(
        CompiledCommandContext context,
        JObject args,
        CancellationToken cancellationToken
    );
}

internal sealed record CompiledCommandResult(
    bool Succeeded,
    JObject Output,
    IReadOnlyList<ArtifactRef> Artifacts,
    IReadOnlyList<ControlCommandError> Diagnostics
);

internal sealed class CompiledCommandContext
{
    public string RequestId { get; }
    public TimeSpan? Timeout { get; }
    public string? JobId { get; }
    public Action<JObject?, string?>? ProgressSink { get; }   // null for sync
    public IArtifactWriter Artifacts { get; }
    // …
}

/// <summary>Bridges a typed handler into the compiled dispatch shape.</summary>
internal sealed class TypedCommandAdapter<TArgs, TOutput> : ICompiledControlCommand
{
    private readonly IControlCommandHandler<TArgs, TOutput> _inner;
    private readonly JsonSerializer _serializer;
    private readonly IControlCommandValidator _validator;
    public ControlCommandDescriptor Descriptor { get; }

    public TypedCommandAdapter(
        IControlCommandHandler<TArgs, TOutput> inner,
        JsonSerializer serializer,
        IControlCommandValidator validator)
    {
        _inner = inner;
        _serializer = serializer;
        _validator = validator;
        Descriptor = new ControlCommandDescriptor(
            name: inner.Name,
            version: inner.Version,
            kind: inner.Kind,
            mutatesState: inner.MutatesState,
            argsSchema: SchemaCache.For<TArgs>(),
            resultSchema: SchemaCache.For<TOutput>(),
            artifactsSchema: SchemaCache.AnyObject
        );
    }

    public async ValueTask<CompiledCommandResult> ExecuteAsync(
        CompiledCommandContext compiledContext,
        JObject args,
        CancellationToken ct)
    {
        // 1. Validate args against the descriptor's args schema.
        var validation = _validator.Validate(args, Descriptor.ArgsSchema);
        if (!validation.Ok)
        {
            return new CompiledCommandResult(
                Succeeded: false,
                Output: new JObject(),
                Artifacts: Array.Empty<ArtifactRef>(),
                Diagnostics: new[] { validation.ToDiagnostic() }
            );
        }

        // 2. Deserialize typed args. EmptyArgs is special-cased.
        TArgs typed = typeof(TArgs) == typeof(EmptyArgs)
            ? default!
            : args.ToObject<TArgs>(_serializer)
                ?? throw new InvalidOperationException(
                    $"Newtonsoft deserialized {typeof(TArgs).Name} as null.");

        // 3. Build the typed context. Job dispatch routes progress
        //    through compiledContext.ProgressSink; sync commands get a
        //    silent IProgress<>.
        var typedContext = new ControlCommandContext(
            requestId: compiledContext.RequestId,
            timeout: compiledContext.Timeout,
            jobId: compiledContext.JobId,
            progress: compiledContext.ProgressSink is null
                ? SilentProgress.Instance
                : new ProgressSinkAdapter(compiledContext.ProgressSink),
            artifacts: compiledContext.Artifacts
        );

        // 4. Run the handler.
        var typedResult = await _inner
            .ExecuteAsync(typedContext, typed, ct)
            .ConfigureAwait(true);  // stay on the Unity sync context

        // 5. Project the typed result back to the wire shape.
        return new CompiledCommandResult(
            Succeeded: typedResult.Succeeded,
            Output: typedResult.Output is null
                ? new JObject()
                : JObject.FromObject(typedResult.Output, _serializer),
            Artifacts: ToArtifactList(typedResult.Artifacts),
            Diagnostics: ToErrorList(typedResult.Diagnostics)
        );
    }
}
```

The adapter:

- **Validates first.** Validation failures never reach the handler.
- **Honors `EmptyArgs`** without paying for deserialization.
- **`ConfigureAwait(true)`** so continuations come back on the Unity sync context.
- **Projects the artifact dict to the existing top-level wire list** using each entry's key as the
  artifact's `LogicalName`. Existing clients reading top-level `artifacts` see the same shape.

### 4.2 Schema cache

```csharp
namespace HotRepl.Control;

public static class SchemaCache
{
    private static readonly ConcurrentDictionary<Type, JObject> Cache = new();

    public static JObject AnyObject { get; } = JObject.Parse(
        "{ \"type\": \"object\", \"additionalProperties\": true }"
    );

    public static JObject EmptyObject { get; } = JObject.Parse(
        "{ \"type\": \"object\", \"additionalProperties\": false }"
    );

    public static JObject For<T>() => For(typeof(T));

    public static JObject For(Type type) => Cache.GetOrAdd(type, BuildSchema);

    private static JObject BuildSchema(Type type)
    {
        if (type == typeof(EmptyArgs)) return EmptyObject;

        var schema = NJsonSchema.JsonSchema.FromType(type, BuilderSettings);
        return JObject.Parse(schema.ToJson());
    }

    private static readonly NJsonSchema.Generation.JsonSchemaGeneratorSettings
        BuilderSettings = new()
        {
            SerializerSettings = ProtocolJsonSerializerSettings.Instance,
            DefaultReferenceTypeNullHandling =
                NJsonSchema.Generation.ReferenceTypeNullHandling.Null,
            AllowReferencesWithProperties = false,   // inline; no $ref
        };
}
```

The schema engine is **NJsonSchema 10.9.0**, pinned. The 10.x line uses only `Newtonsoft.Json` and
`Namotion.Reflection` — both pure .NET, both known-good on Mono Unity. NJsonSchema 11.x adds a
transitive `System.Text.Json` dependency which doesn't work cleanly on Unity Mono, so we
deliberately don't upgrade.

NJsonSchema is **ILRepack-internalized** into `HotRepl.Core.dll` (see §6). `Namotion.Reflection.dll`
stays side-by-side because internalizing its `IsExternalInit` polyfill collides with Core's own
polyfill under the pinned ILRepack task.

NJsonSchema honors these attributes out of the box; we don't add any custom attribute
interpretation:

| Attribute                                              | Effect on schema                                                                        |
| ------------------------------------------------------ | --------------------------------------------------------------------------------------- |
| `[Required]` (`System.ComponentModel.DataAnnotations`) | Property added to parent's `required` array.                                            |
| `[Range(min, max)]`                                    | `minimum` and `maximum` added to numeric schemas.                                       |
| `[Description("…")]` (`System.ComponentModel`)         | Goes into `description`.                                                                |
| `[JsonProperty("wireName")]` (Newtonsoft)              | Property renamed in schema.                                                             |
| `[JsonIgnore]` (Newtonsoft)                            | Property omitted from schema.                                                           |
| `[StringLength(min, max)]`                             | `minLength` / `maxLength` on string schemas.                                            |
| `Nullable<T>` / `string?` (NRT)                        | Property added to schema; `null` allowed via `DefaultReferenceTypeNullHandling = Null`. |

### 4.3 Server-side validation

A single `IControlCommandValidator` interface internal to `HotRepl.Core`. The default implementation
uses NJsonSchema's `JsonSchemaValidator`. The adapter calls it before deserialization.

```csharp
internal interface IControlCommandValidator
{
    ValidationResult Validate(JObject args, JObject schema);
}

internal readonly struct ValidationResult
{
    public bool Ok { get; }
    public IReadOnlyList<NJsonSchema.Validation.ValidationError> Errors { get; }
    public ControlCommandError ToDiagnostic() => new(
        Kind: "validation_failed",
        Code: "argsSchemaViolation",
        Message: Errors.FirstOrDefault()?.ToString() ?? "Argument schema validation failed.",
        Retryable: false,
        Details: BuildDetails(Errors)
    );
}
```

Validation failure → typed handler is not invoked → wire shape: `status = "failed"`, top-level
`diagnostics[0].kind = "validation_failed"`, detail enumerates the schema errors. Clients can render
or retry.

This replaces the v2 pattern where each handler had to manually re-validate args after
deserialization, with no consistency in error shape.

## 5. UnityCommands plugin

First-party demo plugin. Ships bundled with the host plugin builds.

### 5.1 Layout

```
src/HotRepl.UnityCommands/                 ← shared source folder, no csproj
  Commands/
    UnityAppInfoCommand.cs
    UnityGameObjectFindCommand.cs
    UnityTimeSetScaleCommand.cs
    UnityScreenshotCommand.cs
  Screenshots/
    CapturedScreenshot.cs
    EndOfFrameUnityScreenshotCapturer.cs
    IUnityScreenshotCapturer.cs
    UnityPngEncoder.cs
    UnityScreenshotCaptureResult.cs
    UnityScreenshotFailureKind.cs
    UnsupportedUnityScreenshotCapturer.cs
  Models/
    UnityAppInfo.cs
    UnityGameObjectFindArgs.cs
    UnityGameObjectFindResult.cs
    UnityGameObject.cs
    UnitySetTimeScaleArgs.cs
    UnitySetTimeScaleResult.cs
    UnityScreenshotArgs.cs
    UnityScreenshotResult.cs
  Vec3.cs                                  ← shared POCO replacing UnityEngine.Vector3 in schemas
  UnityCommandCatalog.cs                   ← static catalog used by both loader plugins
  UnityCommandCatalogNames.cs              ← command-name constants testable without UnityEngine refs

src/HotRepl.UnityCommands.BepInEx/         ← loader-specific csproj, Mono UnityEngine ref
  Plugin.cs                                ← BaseUnityPlugin with [BepInPlugin] + [BepInDependency("hotrepl.bepinex", HardDependency)]
  HotRepl.UnityCommands.BepInEx.csproj     ← <Compile Include="..\HotRepl.UnityCommands\**\*.cs" Exclude="**\bin\**;**\obj\**" />

src/HotRepl.UnityCommands.MelonLoader/     ← loader-specific csproj, IL2CPP-unhollowed UnityEngine ref
  UnityCommandsMod.cs                      ← MelonMod with [MelonInfo], registers in OnLateInitializeMelon()
  HotRepl.UnityCommands.MelonLoader.csproj ← same <Compile Include> pattern as BepInEx
```

The shared source folder has no .csproj. Both loader csprojs compile the same source against their
respective `UnityEngine.dll`. Standard dual-runtime mod pattern (UnityExplorer, RuntimeUnityEditor).
Both loader csprojs reference `System.ComponentModel.Annotations` so `[Required]` and `[Range]`
attributes compile and are present for schema reflection at runtime.

### 5.2 The four demo commands

Each maps to one architectural pattern. The `Mutates`/`Kind` columns show what's exercised; the
"Pattern shown" column tells a copying contributor what to use this command as a starting point for.

| Command                    | Mutates | Kind | Pattern shown                                                                                                                                                                                                                                                                |
| -------------------------- | ------- | ---- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `unity.app.info`           | no      | Sync | Simplest case: `EmptyArgs`, structured `TOutput`, read-only. Copy when adding a no-arg read.                                                                                                                                                                                 |
| `unity.gameobject.find`    | no      | Sync | Typed args with `[Required]`/`[Description]`, nullable `TOutput`. Copy when adding a read with arguments.                                                                                                                                                                    |
| `unity.time.set_scale`     | yes     | Sync | `MutatesState = true` propagates to MCP `destructiveHint`. `[Range(0, 100)]` is validated server-side before the handler runs. Copy when adding a mutating sync command.                                                                                                     |
| `unity.screenshot.capture` | no      | Job  | Waits for end-of-frame through a loader-provided coroutine bridge, writes the PNG via `context.Artifacts.WriteAsync`, and returns the `ArtifactRef` in the result wrapper's `Artifacts` dict. Copy when adding a Unity workflow that spans frames or produces binary output. |

Concrete shapes (illustrative — final shape on review):

```csharp
public sealed class UnityAppInfoCommand
    : IControlCommandHandler<EmptyArgs, UnityAppInfo>
{
    public string             Name         => "unity.app.info";
    public int                Version      => 1;
    public ControlCommandKind Kind         => ControlCommandKind.Synchronous;
    public bool               MutatesState => false;

    public ValueTask<ControlCommandResult<UnityAppInfo>> ExecuteAsync(
        ControlCommandContext _,
        EmptyArgs __,
        CancellationToken ___)
        => new(ControlCommandResult.Ok(new UnityAppInfo
        {
            ProductName  = UnityEngine.Application.productName,
            UnityVersion = UnityEngine.Application.unityVersion,
            Platform     = UnityEngine.Application.platform.ToString(),
            IsEditor     = UnityEngine.Application.isEditor,
        }));
}
```

```csharp
public sealed class UnitySetTimeScaleCommand
    : IControlCommandHandler<UnitySetTimeScaleArgs, UnitySetTimeScaleResult>
{
    public string             Name         => "unity.time.set_scale";
    public int                Version      => 1;
    public ControlCommandKind Kind         => ControlCommandKind.Synchronous;
    public bool               MutatesState => true;

    public ValueTask<ControlCommandResult<UnitySetTimeScaleResult>> ExecuteAsync(
        ControlCommandContext _,
        UnitySetTimeScaleArgs args,
        CancellationToken __)
    {
        var previous = UnityEngine.Time.timeScale;
        UnityEngine.Time.timeScale = args.TimeScale;
        return new(ControlCommandResult.Ok(
            new UnitySetTimeScaleResult
            {
                PreviousTimeScale = previous,
                NewTimeScale      = UnityEngine.Time.timeScale,
            }));
    }
}

public sealed class UnitySetTimeScaleArgs
{
    [Required, Range(0f, 100f)]
    [Description(
        "New Time.timeScale value. 0 = paused, 1 = normal, " +
        "2 = double-speed. Values > 1 may exceed safe physics-step bounds.")]
    public float TimeScale { get; set; }
}
```

```csharp
public sealed class UnityScreenshotCommand
    : IControlCommandHandler<UnityScreenshotArgs, UnityScreenshotResult>
{
    private readonly IUnityScreenshotCapturer _capturer;

    public string             Name         => "unity.screenshot.capture";
    public int                Version      => 1;
    public ControlCommandKind Kind         => ControlCommandKind.Job;
    public bool               MutatesState => false;

    public async ValueTask<ControlCommandResult<UnityScreenshotResult>> ExecuteAsync(
        ControlCommandContext context,
        UnityScreenshotArgs args,
        CancellationToken ct)
    {
        var capture = await _capturer
            .CaptureAsync(Math.Max(1, args.SuperSize), ct)
            .ConfigureAwait(true);

        if (!capture.Succeeded)
        {
            return Failure(capture.FailureKind);
        }

        var screenshot = capture.Screenshot;
        var artifact = await context.Artifacts.WriteAsync(
            "screenshot",
            screenshot.Png,
            "image/png",
            ct).ConfigureAwait(true);

        return ControlCommandResult.Ok(
            new UnityScreenshotResult { Width = screenshot.Width, Height = screenshot.Height },
            "screenshot",
            artifact);
    }
}
```

Note the result wrapper's `Ok(output, artifactName, artifact)` overload — keeps the screenshot path
concise without sacrificing explicitness.

The loader plugins pass an `EndOfFrameUnityScreenshotCapturer` into the catalog. That capturer
starts a coroutine, waits for `WaitForEndOfFrame`, then either calls
`ScreenCapture.CaptureScreenshotAsTexture` via reflection or falls back to `ReadPixels` for
`superSize = 1`. If only the fallback is available and the request asks for supersampling, the
handler returns `precondition_failed` with code `screenshotSuperSizeUnsupported` instead of silently
returning an unscaled image. The command is intentionally `ControlCommandKind.Job`; making it
synchronous would block `ReplEngine.Tick()` and prevent the coroutine from ever reaching
end-of-frame.

### 5.3 Loader plugins

```csharp
// HotRepl.UnityCommands.BepInEx/Plugin.cs
[BepInPlugin("hotrepl.unitycommands.bepinex", "HotRepl Unity Commands", VersionInfo.Version)]
[BepInDependency("hotrepl.bepinex", BepInDependency.DependencyFlags.HardDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    private readonly List<IDisposable> _registrations = new();

    private void Awake()
    {
        var enabled = Config.Bind(
            "General", "Enabled", true,
            "Master switch. When false, no UnityCommands handlers are registered. " +
            "Changes apply on next game start."
        );
        var disabled = Config.Bind(
            "Commands", "Disabled", "",
            "Comma-separated list of command names to skip (e.g. " +
            "'unity.time.set_scale, unity.screenshot.capture')."
        );

        if (!enabled.Value)
        {
            Logger.LogInfo("HotRepl.UnityCommands disabled via config; skipping registration.");
            return;
        }

        var skip = ParseCsv(disabled.Value);
        var factories = UnityCommandCatalog.Build(
            new EndOfFrameUnityScreenshotCapturer(routine =>
            {
                StartCoroutine(routine);
            }));
        var names = UnityCommandCatalog.Names;
        for (var i = 0; i < factories.Count; i++)
        {
            if (skip.Contains(names[i])) continue;
            _registrations.Add(factories[i](GlobalControlCommandRegistry.Instance));
        }
    }

    private void OnDestroy()
    {
        foreach (var r in _registrations) r.Dispose();
        _registrations.Clear();
    }
}
```

The MelonLoader variant is structurally identical: `MelonPreferences` instead of `Config.Bind`,
`OnLateInitializeMelon` instead of `Awake`, `OnDeinitializeMelon` instead of `OnDestroy`.

### 5.4 Distribution

The `HotRepl.UnityCommands.BepInEx.csproj` is referenced from `HotRepl.BepInEx.csproj`. The host
build runs a sidecar-copy target for `HotRepl.UnityCommands.BepInEx.*` and the BepInEx ILRepack
cleanup allowlist preserves those files in `bin/Debug/netstandard2.1/` alongside
`HotRepl.BepInEx.dll`. Existing consumer deploy scripts that copy "everything from the build output"
pick it up automatically with no changes.

BepInEx scans `BepInEx/plugins/` for assemblies with `[BepInPlugin]` attributes, so the
UnityCommands DLL is loaded as a peer plugin (not merged into HotRepl.BepInEx). The
`[BepInDependency]` enforces load order.

The MelonLoader variant uses the same pattern with `HotRepl.UnityCommands.MelonLoader.csproj`
referenced and copied from `HotRepl.Host.MelonLoader.csproj`.

## 6. Build pipeline — ILRepack details

`HotRepl.Core.csproj` declares the NJsonSchema package reference as private, with ILRepack running
post-build to internalize it.

```xml
<!-- HotRepl.Core.csproj -->
<ItemGroup>
  <PackageReference Include="NJsonSchema"                 Version="10.9.0" PrivateAssets="all" />
  <PackageReference Include="Namotion.Reflection"         Version="2.1.2" />
  <PackageReference Include="ILRepack.Lib.MSBuild.Task"   Version="2.0.34.2" PrivateAssets="all" />
</ItemGroup>
```

`Namotion.Reflection 2.1.2` is NJsonSchema 10.9.0's only non-Newtonsoft dependency. It stays as a
normal consumer-facing dependency because its own `IsExternalInit` polyfill collides with Core's
polyfill when internalized. The output cleanup keeps `Namotion.Reflection.dll` and removes only
NJsonSchema plus Unity/Mono-incompatible transitive facades.

```xml
<!-- ILRepack.targets — sibling of HotRepl.Core.csproj -->
<Target Name="ILRepackCore" AfterTargets="Build">
  <PropertyGroup>
    <CoreOutput>$(OutputPath)HotRepl.Core.dll</CoreOutput>
  </PropertyGroup>
  <ItemGroup>
    <ILRepackInput Include="$(CoreOutput)" />
    <ILRepackInput Include="$(OutputPath)NJsonSchema.dll" />
  </ItemGroup>
  <ItemGroup>
    <ILRepackDoNotInternalize Include="HotRepl.Protocol" />
    <ILRepackDoNotInternalize Include="Newtonsoft.Json" />
    <ILRepackDoNotInternalize Include="Microsoft.CSharp" />
    <ILRepackDoNotInternalize Include="UnityEngine" />
    <ILRepackDoNotInternalize Include="UnityEngine.CoreModule" />
    <ILRepackDoNotInternalize Include="BepInEx" />
    <ILRepackDoNotInternalize Include="MelonLoader" />
    <ILRepackDoNotInternalize Include="Fleck" />
    <ILRepackDoNotInternalize Include="netstandard" />
    <ILRepackDoNotInternalize Include="mscorlib" />
    <ILRepackDoNotInternalize Include="System" />
    <ILRepackDoNotInternalize Include="System.Core" />
    <ILRepackDoNotInternalize Include="System.Runtime" />
  </ItemGroup>
  <ItemGroup>
    <ILRepackLibPath Include="$(OutputPath)" />
  </ItemGroup>
  <ILRepack
    Parallel="true"
    Internalize="true"
    InternalizeExclude="@(ILRepackDoNotInternalize)"
    LibraryPath="@(ILRepackLibPath)"
    InputAssemblies="@(ILRepackInput)"
    TargetKind="Dll"
    OutputFile="$(CoreOutput)"
  />
  <ItemGroup>
    <_HotReplCoreKeep Include="HotRepl.Core.dll" />
    <_HotReplCoreKeep Include="HotRepl.Core.pdb" />
    <_HotReplCoreKeep Include="HotRepl.Core.xml" />
    <_HotReplCoreKeep Include="HotRepl.Core.deps.json" />
    <_HotReplCoreKeep Include="HotRepl.Protocol.dll" />
    <_HotReplCoreKeep Include="HotRepl.Protocol.pdb" />
    <_HotReplCoreKeep Include="HotRepl.Protocol.xml" />
    <_HotReplCoreKeep Include="Newtonsoft.Json.dll" />
    <_HotReplCoreKeep Include="Fleck.dll" />
    <_HotReplCoreKeep Include="Namotion.Reflection.dll" />
    <_HotReplCoreOutputs Include="$(OutputPath)*.dll" />
    <_HotReplCoreOutputs Include="$(OutputPath)*.pdb" />
    <_HotReplCoreOutputs Include="$(OutputPath)*.xml" />
    <_HotReplCoreDelete
      Include="@(_HotReplCoreOutputs)"
      Exclude="@(_HotReplCoreKeep -> '$(OutputPath)%(Identity)')"
    />
  </ItemGroup>
  <Delete Files="@(_HotReplCoreDelete)" />
</Target>
```

After build:

- `HotRepl.Core.dll` includes internalized NJsonSchema (same name, same public API surface).
- `NJsonSchema.dll` is **not** in the build output.
- `Namotion.Reflection.dll`, `Newtonsoft.Json.dll`, and `Fleck.dll` are still in the build output
  because consumers/runtime code reference them directly.
- `Microsoft.CSharp.dll` is **not** in the build output (it resolves from the Mono/Unity runtime BCL
  at load time).

Consumer plugin-folder shape gains `Namotion.Reflection.dll` relative to v2; no `System.Text.Json`
dependency is introduced.

## 7. Configuration

UnityCommands and the template both use the native per-loader config system, not a HotRepl-rolled
config.

| Key        | Section / Category                                            | Default | Purpose                                                                                                                  |
| ---------- | ------------------------------------------------------------- | ------- | ------------------------------------------------------------------------------------------------------------------------ |
| `Enabled`  | `[General]` (BepInEx) / `HotRepl.UnityCommands` (MelonLoader) | `true`  | Master switch. False = register nothing.                                                                                 |
| `Disabled` | `[Commands]` / `HotRepl.UnityCommands`                        | empty   | Comma-separated command names to skip. Useful for consumer mods that want to override a UnityCommand with the same name. |

Config is read **once at startup**. Changes require a game restart. The config description text says
so. This is the same pattern every BepInEx/MelonLoader plugin uses for non-runtime-tunable settings,
and avoids the genuine difficulty of safely tearing down handlers mid-flight while jobs are running.

## 8. Mod template (`glockyco/hotrepl-mod-template`)

A separate GitHub repo (must be separate — `Use this template` is a whole-repo GitHub feature). It
demonstrates the canonical authoring pattern using shared-source compilation, the same way
UnityCommands does. No `MyMod.Core` csproj with a Unity dependency.

### 8.1 Layout

```
hotrepl-mod-template/
├── .template.config/
│   └── template.json                       ← dotnet new metadata
├── .github/workflows/ci.yml                ← build verification on PRs
├── src/
│   ├── MyMod/                              ← shared source folder, no csproj
│   │   ├── Commands/
│   │   │   └── HelloWorldCommand.cs
│   │   ├── Models/
│   │   │   ├── HelloWorldArgs.cs
│   │   │   └── HelloWorldResult.cs
│   │   └── MyModCatalog.cs
│   ├── MyMod.BepInEx/                      ← loader-specific csproj
│   │   ├── Plugin.cs
│   │   ├── PluginInfo.cs
│   │   └── MyMod.BepInEx.csproj
│   └── MyMod.MelonLoader/                  ← loader-specific csproj
│       ├── MyModMelonMod.cs
│       └── MyMod.MelonLoader.csproj
├── scripts/
│   ├── deploy-bepinex.sh
│   └── deploy-melonloader.sh
├── .editorconfig
├── .gitignore
├── Directory.Build.props                   ← shared TFM, treat warnings as errors
├── Local.props.example                     ← game-path config (gitignored real file)
├── LICENSE
├── MyMod.sln
└── README.md
```

### 8.2 `dotnet new` manifest

```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "glockyco",
  "classifications": ["Unity", "BepInEx", "MelonLoader", "Plugin", "HotRepl"],
  "identity": "Glockyco.HotRepl.ModTemplate",
  "name": "HotRepl Mod (BepInEx + MelonLoader)",
  "shortName": "hotrepl-mod",
  "sourceName": "MyMod",
  "tags": { "language": "C#", "type": "project" },
  "symbols": {
    "PluginGuid": {
      "type": "parameter",
      "datatype": "string",
      "description": "BepInEx plugin GUID (recommended: '<lower-vendor>.<lower-modname>')",
      "defaultValue": "yourvendor.yourmod",
      "replaces": "PLUGIN_GUID_PLACEHOLDER"
    },
    "Author": {
      "type": "parameter",
      "datatype": "string",
      "description": "Mod author (used in MelonInfo and README)",
      "defaultValue": "Your Name",
      "replaces": "AUTHOR_PLACEHOLDER"
    }
  }
}
```

Installation + usage:

```bash
dotnet new install ~/Projects/hotrepl-mod-template
dotnet new hotrepl-mod -n AwesomeMod -o ~/Projects/AwesomeMod \
  --PluginGuid awesomestudios.awesomemod \
  --Author "Awesome Studios"
```

### 8.3 The demo command

One file demonstrates the v3 pattern end-to-end. New plugin authors copy this and edit names:

```csharp
// src/MyMod/Commands/HelloWorldCommand.cs
using HotRepl.Control;
using MyMod.Models;
using System.Threading;
using System.Threading.Tasks;

namespace MyMod.Commands;

/// <summary>
/// Demo command — replace with your own.
///
/// The canonical typed-command pattern:
///   1. Define POCO args and result types (see Models/).
///   2. Implement IControlCommandHandler&lt;TArgs, TResult&gt;.
///   3. Register from the loader plugin (see ../MyMod.BepInEx/Plugin.cs).
///
/// Invoke from any HotRepl client:
///   $ hotrepl run mymod.hello '{}'
///   $ hotrepl run mymod.hello '{"name":"World"}'
/// </summary>
public sealed class HelloWorldCommand
    : IControlCommandHandler<HelloWorldArgs, HelloWorldResult>
{
    public string             Name         => "mymod.hello";
    public int                Version      => 1;
    public ControlCommandKind Kind         => ControlCommandKind.Synchronous;
    public bool               MutatesState => false;

    public ValueTask<ControlCommandResult<HelloWorldResult>> ExecuteAsync(
        ControlCommandContext _,
        HelloWorldArgs args,
        CancellationToken __)
    {
        var who = string.IsNullOrWhiteSpace(args.Name) ? "world" : args.Name;
        return new(ControlCommandResult.Ok(new HelloWorldResult
        {
            Greeting = $"Hello, {who}!",
            GeneratedAt = System.DateTimeOffset.UtcNow,
        }));
    }
}
```

### 8.4 Loader plugins, scripts, and README

The two loader plugins follow exactly the same pattern as UnityCommands in §5.3. The deploy scripts
copy each loader's built DLL into the configured game directory (read from `Local.props`).

README structure:

1. What you're getting (3 sentences).
2. Prerequisites (.NET SDK, BepInEx or MelonLoader installed, HotRepl).
3. Quickstart (3 commands: clone or `dotnet new`, edit `Local.props`, run
   `scripts/deploy-bepinex.sh`).
4. Project layout (table).
5. Adding a new command (link to `HelloWorldCommand.cs`, say "copy this file, change the names").
6. Schema authoring (what the standard `[Description]`/`[Required]`/ `[Range]` attributes do).
7. Mono vs IL2CPP (when shared source isn't enough — link to
   `src/HotRepl.UnityCommands.MelonLoader/UnityCommandsMod.cs` as the reference for "what
   diverges").

## 9. Verification

The Phase 1 work is done when:

- `bun run --filter './packages/*' test` and `dotnet test
  tests/HotRepl.Tests/` are green.
- `lefthook run pre-push --force` is green.
- `HotRepl.Core 3.0.0` ILRepack output: `NJsonSchema.dll` is not in the output; `HotRepl.Core.dll`,
  `HotRepl.Protocol.dll`, `Newtonsoft.Json.dll`, `Fleck.dll`, and `Namotion.Reflection.dll` are
  present.
- A fresh BepInEx install with `HotRepl.BepInEx.dll` and `HotRepl.UnityCommands.BepInEx.dll`
  deployed: starting a game with the plugin loaded shows the 4 unity.* commands in
  `bunx @hotrepl/cli list-commands` output.
- Same on the MelonLoader side with `HotRepl.UnityCommands.MelonLoader.dll`.
- `dotnet new install` + `dotnet new hotrepl-mod -n SmokeTest` produces a project that
  `dotnet build`s clean.
- Server-side validation: piping
  `{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"unity.time.set_scale","arguments":{"timeScale":-1}}}`
  through the MCP bin returns a `validation_failed` diagnostic, NOT a handler exception, and
  `Time.timeScale` is unchanged.

## 10. Tests

New xUnit tests under `tests/HotRepl.Tests/Unit/`:

- `TypedCommandAdapterTests`: round-trip `EmptyArgs`, nullable `TOutput`, `[Required]` violation,
  `[Range]` violation, attribute rename via `[JsonProperty]`, multi-artifact result,
  validation-failed result vs handler-thrown exception.
- `SchemaCacheTests`: single-build per type, `EmptyArgs` special case, `AnyObject` sentinel,
  attribute coverage.
- `ControlCommandValidatorTests`: schema-violation path, missing-required path, range-violation
  path, nested-object validation.
- `RegistryRegisterGenericTests`: `Register<TArgs,TOutput>` correctly wraps and the resulting
  `Describe()` matches the typed handler's declared name/version/kind/mutates.
- `UnityCommandsCatalogTests`: exactly 4 handlers, names match spec, `MutatesState` matches spec for
  each.

Existing conformance tests under `packages/conformance/test/` must continue to pass against the v3
wire shape (which is the same wire shape — only the C#-side authoring changes; the JSON-RPC envelope
is unchanged).

## 11. Open questions to revisit in Phase 2

- The exact `IArtifactWriter` storage backend (in-memory ring buffer vs. temp directory under the
  host's plugin folder) needs a concrete pick. Phase 1 declares the interface; Phase 2 picks the
  default implementation and tests it through Ardenfall's `run.finalize` (which writes multiple
  ~100KB artifacts).
- Whether `ControlCommandResult.Ok<T>` overloads should also exist for `IEnumerable<ArtifactRef>`
  (vs only `IReadOnlyDictionary<>`). Phase 2 decides based on what Ardenfall's existing handlers
  actually want.
- Whether handlers should be allowed to set wire `status = "failed"` WITH an `Output` (e.g. "the
  export completed with diagnostics but here's what we got"). Current design says no:
  `Succeeded = false` implies `Output = null` and only diagnostics carry information. Phase 2
  verifies this against Ardenfall's partial-failure cases.

These don't block Phase 1 from shipping; they get answered as Phase 2 puts real workloads through
the API.
