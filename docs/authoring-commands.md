# Authoring HotRepl commands

Typed commands are the stable automation surface for HotRepl. They expose JSON-schema-validated
arguments, typed outputs, job status, and artifact references over the existing v2 control protocol.
The canonical reference implementation lives in `src/HotRepl.UnityCommands/Commands/`; the BepInEx
and MelonLoader sample projects show how to package the same command catalog for Unity loaders.

## Command handler shape

A command handler is a C# class implementing `IControlCommandHandler<TArgs, TOutput>`. Declare the
stable wire metadata with `[ControlCommand]` on the handler type:

```csharp
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using Newtonsoft.Json;

[ControlCommand("yourmod.example", Version = 1, Kind = ControlCommandKind.Sync)]
public sealed class ExampleCommand : IControlCommandHandler<ExampleArgs, ExampleResult>
{
    public string Name => "yourmod.example";
    public int Version => 1;
    public ControlCommandKind Kind => ControlCommandKind.Sync;
    public bool MutatesState => false;

    public ValueTask<ControlCommandResult<ExampleResult>> ExecuteAsync(
        ControlCommandContext<ExampleResult> context,
        ExampleArgs args,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(args.Name))
        {
            return new ValueTask<ControlCommandResult<ExampleResult>>(
                context.ValidationFailed("nameRequired", "name must not be empty."));
        }

        var output = new ExampleResult { Reply = $"hello {args.Name}" };
        return new ValueTask<ControlCommandResult<ExampleResult>>(context.Ok(output));
    }
}
```

The interface metadata properties remain part of the authoring contract. When `[ControlCommand]` is
present, HotRepl uses the attribute values for the exposed descriptor so metadata is colocated with
the handler type.

Use `ControlCommandKind.Sync` for commands that complete in one request/response. Use
`ControlCommandKind.Job` for long-running work that reports progress and completes through
`job_status`.

Set `MutatesState = true` for commands that change game or runtime state. MCP tools use that flag to
set destructive-operation annotations.

## Argument and output DTOs

Arguments and outputs are plain Newtonsoft-serialized DTOs. HotRepl generates JSON Schema from the
DTO shape and validates arguments before the handler runs.

```csharp
public sealed class ExampleArgs
{
    [JsonProperty("name", Required = Required.Always)]
    public string Name { get; set; } = "";
}

public sealed class ExampleResult
{
    [JsonProperty("reply", Required = Required.Always)]
    public string Reply { get; set; } = "";
}
```

Use `EmptyArgs` for commands that take no arguments. Use Newtonsoft attributes such as
`[JsonProperty]`, and standard validation attributes such as `[Required]`, `[Range]`, and
`[Description]`, when clients need schema-visible contracts.

## Returning results

`ControlCommandContext<TOutput>` carries request metadata, progress reporting, and the artifact
writer. It also exposes result helpers bound to the handler output type:

```csharp
return context.Ok(new ExampleResult { Reply = "ready" });
return context.ValidationFailed("invalidName", "name must not be empty.");
return context.PreconditionFailed("sceneMissing", "the active Unity scene is not loaded.");
```

Use `context.Failed(...)` when the command needs to construct a full `ControlCommandDiagnostic`.
Prefer `ValidationFailed` for bad caller input and `PreconditionFailed` for valid input that cannot
run in the current game state.

## Reporting job progress

Job-kind commands report progress through the context. Progress snapshots are JSON objects so
clients can render structured state while the job runs.

```csharp
[ControlCommand("yourmod.export", Kind = ControlCommandKind.Job, MutatesState = true)]
public sealed class ExportCommand : IControlCommandHandler<ExportArgs, ExportResult>
{
    public string Name => "yourmod.export";
    public int Version => 1;
    public ControlCommandKind Kind => ControlCommandKind.Job;
    public bool MutatesState => true;

    public async ValueTask<ControlCommandResult<ExportResult>> ExecuteAsync(
        ControlCommandContext<ExportResult> context,
        ExportArgs args,
        CancellationToken cancellationToken)
    {
        context.Progress.Report(new ControlCommandProgress(
            Snapshot: new JObject { ["phase"] = "loading" },
            Message: "Loading data."));

        await ExportAsync(args, cancellationToken);
        return context.Ok(new ExportResult { Completed = true });
    }
}
```

## Attaching artifacts

Artifacts are references, not bulk payloads in the control response. Handlers attach bytes, streams,
or existing files through `context.Artifacts` and return the resulting `ArtifactRef` under a logical
artifact key.

```csharp
var artifact = await context.Artifacts.AttachFileAsync(
    logicalName: "report.json",
    path: reportPath,
    contentType: "application/json",
    cancellationToken);

return context.Ok(output, "report", artifact);
```

Use the attachment method that matches the source data:

- `AttachBytesAsync` for already-materialized byte buffers.
- `AttachStreamAsync` for generated data that should be hashed while streaming.
- `AttachFileAsync` for files already written on disk.

Declare expected artifact keys on the handler so `command_describe` exposes the artifact schema:

```csharp
[ControlCommand("yourmod.report", Kind = ControlCommandKind.Job)]
[ControlCommandArtifact("report", ContentType = "application/json", Required = true)]
public sealed class ReportCommand : IControlCommandHandler<ReportArgs, ReportResult>
{
    // ...
}
```

A key pattern such as `data.<stem>` describes a family of artifact keys. Exact required keys appear
in the descriptor's required artifact schema.

## Registering handlers

Register handlers with the global registry from the Unity loader integration. Dispose registrations
when the plugin or mod unloads.

```csharp
using System;
using BepInEx;
using HotRepl.Control;

[BepInPlugin("com.example.yourmod", "Your Mod", "1.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
    private IDisposable? registration;

    private void Awake()
    {
        registration = GlobalControlCommandRegistry.Instance.Register(new ExampleCommand());
    }

    private void OnDestroy() => registration?.Dispose();
}
```

For MelonLoader, register commands from `OnLateInitializeMelon`, after every mod has completed
`OnInitializeMelon`.

## Testing handlers

Use `HotRepl.Testing.HandlerHarness` for in-process handler tests. It builds the same generic
context shape that HotRepl passes at runtime and returns a compact result object for assertions.

```csharp
var result = await HandlerHarness.RunAsync(
    new ExampleCommand(),
    new ExampleArgs { Name = "Ada" });

Assert.True(result.Succeeded);
Assert.Equal("hello Ada", result.Output.Reply);
```

Validate DTO schema behavior separately when a test needs to pin argument contracts:

```csharp
var validation = HandlerHarness.Validate<ExampleArgs>("{}");
Assert.False(validation.Ok);
```

## Canonical samples

The shared command implementations are in `src/HotRepl.UnityCommands/Commands/`:

- `UnityAppInfoCommand` — no-args sync command returning Unity app metadata.
- `UnityGameObjectFindCommand` — typed argument sync query.
- `UnityTimeSetScaleCommand` — mutating sync command.
- `UnityScreenshotCommand` — job command returning a PNG artifact.

`src/HotRepl.UnityCommands.BepInEx` and `src/HotRepl.UnityCommands.MelonLoader` are the canonical
sample packaging projects. They compile the shared command catalog into loader-specific assemblies
without duplicating command logic.
