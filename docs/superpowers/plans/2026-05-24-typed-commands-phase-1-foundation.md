# Typed commands — Phase 1 foundation implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `HotRepl.Core` 3.0.0 with the new strongly-typed command interface, the first-party
`HotRepl.UnityCommands` demo plugin (BepInEx + MelonLoader), and the `glockyco/hotrepl-mod-template`
GitHub scaffolding repo.

**Architecture:** A new public `IControlCommandHandler<TArgs, TOutput>` interface replaces the v2
non-generic shape. An internal `TypedCommandAdapter` projects typed handlers onto the existing wire
dispatch, generating JSON schemas from POCO arg/result types via NJsonSchema 10.9.0
(ILRepack-internalized into `HotRepl.Core.dll`) and validating inbound args server-side before
invoking the handler. Three workstreams: A = Core foundation, B = UnityCommands plugin, C = mod
template repo. B and C depend on A; B and C are independent of each other.

**Tech Stack:** C# / `netstandard2.1` / `Newtonsoft.Json` 13.x / `NJsonSchema` 10.9.0 /
`Namotion.Reflection` 2.1.2 (both ILRepack-internalized) / `ILRepack.Lib.MSBuild.Task` 2.0.x / xUnit
/ BepInEx 5.x / MelonLoader 0.6+ / `dotnet new` template manifest.

**Spec:**
[`docs/superpowers/specs/2026-05-24-typed-commands-phase-1-foundation.md`](../specs/2026-05-24-typed-commands-phase-1-foundation.md).

---

## File map

### Workstream A — `HotRepl.Core` foundation

| File                                                            | Action                                                                                                                              |
| --------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| `Directory.Packages.props`                                      | Modify: add NJsonSchema + Namotion.Reflection pinned versions                                                                       |
| `src/HotRepl.Core/HotRepl.Core.csproj`                          | Modify: add NJsonSchema + Namotion.Reflection + ILRepack.Lib.MSBuild.Task package refs; bump Version to 3.0.0                       |
| `src/HotRepl.Core/ILRepack.targets`                             | Create: post-build merge of NJsonSchema + Namotion.Reflection into HotRepl.Core.dll                                                 |
| `src/HotRepl.Core/Control/EmptyArgs.cs`                         | Create: marker struct                                                                                                               |
| `src/HotRepl.Core/Control/ControlCommandDiagnostic.cs`          | Create: typed diagnostic record + enum                                                                                              |
| `src/HotRepl.Core/Control/ControlCommandResult.cs`              | **Replace**: generic `ControlCommandResult<TOutput>` (the existing non-generic shape becomes `internal CompiledCommandResult`)      |
| `src/HotRepl.Core/Control/Artifacts/IArtifactWriter.cs`         | Create: artifact-writing interface                                                                                                  |
| `src/HotRepl.Core/Control/Artifacts/InMemoryArtifactWriter.cs`  | Create: default in-memory implementation; handed to handlers via context                                                            |
| `src/HotRepl.Core/Control/ControlCommandContext.cs`             | **Replace**: rich context (Progress, Artifacts, JobId in addition to RequestId/Timeout)                                             |
| `src/HotRepl.Core/Control/IControlCommandHandler.cs`            | **Replace**: delete the non-generic interface; create new generic `IControlCommandHandler<TArgs, TOutput>`                          |
| `src/HotRepl.Core/Control/Schema/SchemaCache.cs`                | Create: NJsonSchema-backed schema cache                                                                                             |
| `src/HotRepl.Core/Control/Schema/IControlCommandValidator.cs`   | Create: validation interface                                                                                                        |
| `src/HotRepl.Core/Control/Schema/NJsonSchemaValidator.cs`       | Create: NJsonSchema-backed validator (internal)                                                                                     |
| `src/HotRepl.Core/Control/Internal/ICompiledControlCommand.cs`  | Create: internal dispatch shape (replaces what the old `IControlCommandHandler` was)                                                |
| `src/HotRepl.Core/Control/Internal/CompiledCommandContext.cs`   | Create: internal context handed to ICompiledControlCommand                                                                          |
| `src/HotRepl.Core/Control/Internal/CompiledCommandResult.cs`    | Create: internal result record consumed by the router                                                                               |
| `src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs`      | Create: bridges typed handlers to ICompiledControlCommand                                                                           |
| `src/HotRepl.Core/Control/Internal/SilentProgress.cs`           | Create: no-op `IProgress<ControlCommandProgress>` for sync commands                                                                 |
| `src/HotRepl.Core/Control/Internal/ProgressSinkAdapter.cs`      | Create: adapts the job manager's `Action<JObject?, string?>` to `IProgress<ControlCommandProgress>`                                 |
| `src/HotRepl.Core/Control/IControlCommandRegistry.cs`           | Modify: add `Register<TArgs, TOutput>(IControlCommandHandler<TArgs, TOutput>)`; change `TryGet` to return `ICompiledControlCommand` |
| `src/HotRepl.Core/Control/GlobalControlCommandRegistry.cs`      | Modify: implement new Register; store `ICompiledControlCommand` internally                                                          |
| `src/HotRepl.Core/Control/EmptyControlCommandRegistry.cs`       | Modify: trivial update to satisfy new interface                                                                                     |
| `src/HotRepl.Core/Control/ControlCommandRouter.cs`              | Modify: consume `ICompiledControlCommand`; wire job progress through                                                                |
| `src/HotRepl.Core/Control/Jobs/ControlJobManager.cs`            | Modify: pass `CompiledCommandContext` (with progress sink) instead of `ControlJobExecutionContext.ToCommandContext()`               |
| `src/HotRepl.Core/Control/Jobs/ControlJobExecutionContext.cs`   | Delete: superseded by `CompiledCommandContext`                                                                                      |
| `tests/HotRepl.Tests/Unit/SchemaCacheTests.cs`                  | Create                                                                                                                              |
| `tests/HotRepl.Tests/Unit/ControlCommandValidatorTests.cs`      | Create                                                                                                                              |
| `tests/HotRepl.Tests/Unit/TypedCommandAdapterTests.cs`          | Create                                                                                                                              |
| `tests/HotRepl.Tests/Unit/RegistryTypedRegisterTests.cs`        | Create                                                                                                                              |
| `tests/HotRepl.Tests/Integration/TypedCommandRoundTripTests.cs` | Create: register typed handler → call via router → assert wire shape                                                                |

### Workstream B — `HotRepl.UnityCommands` plugin

| File                                                                             | Action                                                |
| -------------------------------------------------------------------------------- | ----------------------------------------------------- |
| `src/HotRepl.UnityCommands/Vec3.cs`                                              | Create: POCO `{ float X, Y, Z }`                      |
| `src/HotRepl.UnityCommands/Models/UnityAppInfo.cs`                               | Create                                                |
| `src/HotRepl.UnityCommands/Models/UnityGameObjectFindArgs.cs`                    | Create                                                |
| `src/HotRepl.UnityCommands/Models/UnityGameObjectFindResult.cs`                  | Create                                                |
| `src/HotRepl.UnityCommands/Models/UnityGameObject.cs`                            | Create                                                |
| `src/HotRepl.UnityCommands/Models/UnitySetTimeScaleArgs.cs`                      | Create                                                |
| `src/HotRepl.UnityCommands/Models/UnitySetTimeScaleResult.cs`                    | Create                                                |
| `src/HotRepl.UnityCommands/Models/UnityScreenshotArgs.cs`                        | Create                                                |
| `src/HotRepl.UnityCommands/Models/UnityScreenshotResult.cs`                      | Create                                                |
| `src/HotRepl.UnityCommands/Commands/UnityAppInfoCommand.cs`                      | Create                                                |
| `src/HotRepl.UnityCommands/Commands/UnityGameObjectFindCommand.cs`               | Create                                                |
| `src/HotRepl.UnityCommands/Commands/UnityTimeSetScaleCommand.cs`                 | Create                                                |
| `src/HotRepl.UnityCommands/Commands/UnityScreenshotCommand.cs`                   | Create                                                |
| `src/HotRepl.UnityCommands/UnityCommandCatalog.cs`                               | Create: static enumeration used by both loaders       |
| `src/HotRepl.UnityCommands.BepInEx/HotRepl.UnityCommands.BepInEx.csproj`         | Create                                                |
| `src/HotRepl.UnityCommands.BepInEx/Plugin.cs`                                    | Create                                                |
| `src/HotRepl.UnityCommands.MelonLoader/HotRepl.UnityCommands.MelonLoader.csproj` | Create                                                |
| `src/HotRepl.UnityCommands.MelonLoader/Mod.cs`                                   | Create                                                |
| `src/HotRepl.BepInEx/HotRepl.BepInEx.csproj`                                     | Modify: reference `HotRepl.UnityCommands.BepInEx`     |
| `src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj`                   | Modify: reference `HotRepl.UnityCommands.MelonLoader` |
| `tests/HotRepl.Tests/Unit/UnityCommandsCatalogTests.cs`                          | Create: catalog exposes exactly 4 named commands      |

### Workstream C — `hotrepl-mod-template` repo (new, separate)

| File (in new repo)                               | Action                                                |
| ------------------------------------------------ | ----------------------------------------------------- |
| `.gitignore`                                     | Create                                                |
| `.editorconfig`                                  | Create                                                |
| `LICENSE`                                        | Create: MIT                                           |
| `Directory.Build.props`                          | Create: shared TFM + warnings-as-errors + LangVersion |
| `Local.props.example`                            | Create: game-path template                            |
| `MyMod.sln`                                      | Create                                                |
| `src/MyMod/Models/HelloWorldArgs.cs`             | Create                                                |
| `src/MyMod/Models/HelloWorldResult.cs`           | Create                                                |
| `src/MyMod/Commands/HelloWorldCommand.cs`        | Create                                                |
| `src/MyMod/MyModCatalog.cs`                      | Create                                                |
| `src/MyMod.BepInEx/MyMod.BepInEx.csproj`         | Create                                                |
| `src/MyMod.BepInEx/Plugin.cs`                    | Create                                                |
| `src/MyMod.BepInEx/PluginInfo.cs`                | Create                                                |
| `src/MyMod.MelonLoader/MyMod.MelonLoader.csproj` | Create                                                |
| `src/MyMod.MelonLoader/Mod.cs`                   | Create                                                |
| `scripts/deploy-bepinex.sh`                      | Create                                                |
| `scripts/deploy-melonloader.sh`                  | Create                                                |
| `.template.config/template.json`                 | Create                                                |
| `.github/workflows/ci.yml`                       | Create                                                |
| `README.md`                                      | Create                                                |

---

## Workstream A — `HotRepl.Core` foundation

Order matters: tasks A1 → A8 lay the type foundation. A9 (`TypedCommandAdapter`) depends on them.
A10–A12 update the surface and dispatch path. A13 is the cleanup commit.

### A1: Add NJsonSchema package + central versioning

**Files:**

- Create: `Directory.Packages.props` (if not present; otherwise modify)
- Modify: `src/HotRepl.Core/HotRepl.Core.csproj`

- [ ] **Step 1: Check for `Directory.Packages.props`**

```bash
ls Directory.Packages.props 2>/dev/null && cat Directory.Packages.props || echo "ABSENT"
```

If `ABSENT`, create it. If present, just add the new `PackageVersion` entries to the existing
`<ItemGroup>`.

- [ ] **Step 2: Pin NJsonSchema + Namotion.Reflection + ILRepack versions**

If creating `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Fleck"                          Version="1.2.0" />
    <PackageVersion Include="Newtonsoft.Json"                Version="13.0.3" />
    <PackageVersion Include="NJsonSchema"                    Version="10.9.0" />
    <PackageVersion Include="Namotion.Reflection"            Version="2.1.2" />
    <PackageVersion Include="ILRepack.Lib.MSBuild.Task"      Version="2.0.41" />
    <PackageVersion Include="BepInEx.Core"                   Version="5.4.21" />
    <!-- ...existing test packages... -->
  </ItemGroup>
</Project>
```

If `Directory.Packages.props` already exists, just add the four new `PackageVersion` lines
(NJsonSchema, Namotion.Reflection, ILRepack.Lib.MSBuild.Task) matching the existing style.

- [ ] **Step 3: Add package references to Core**

Modify `src/HotRepl.Core/HotRepl.Core.csproj`. In the existing `<ItemGroup>` that holds package
references, add:

```xml
<PackageReference Include="NJsonSchema"                  PrivateAssets="all" />
<PackageReference Include="Namotion.Reflection"          PrivateAssets="all" />
<PackageReference Include="ILRepack.Lib.MSBuild.Task"    PrivateAssets="all" />
```

`PrivateAssets="all"` keeps the package from flowing to consumers — they only see HotRepl.Core.

- [ ] **Step 4: Bump Core version to 3.0.0**

Same csproj, change `<Version>2.0.0</Version>` to `<Version>3.0.0</Version>`. Update `<Description>`
if it mentions "v2":

```xml
<Version>3.0.0</Version>
<Description>Unity-safe HotRepl v3 runtime core with typed command authoring.</Description>
```

- [ ] **Step 5: Verify restore + compile**

```bash
dotnet restore src/HotRepl.Core/HotRepl.Core.csproj
dotnet build src/HotRepl.Core/ --nologo -v q
```

Expected: clean build. (Nothing in the source yet uses NJsonSchema, so it should compile.)

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props src/HotRepl.Core/HotRepl.Core.csproj
git commit -m "build(core): add NJsonSchema and ILRepack dependencies; bump to 3.0.0"
```

---

### A2: ILRepack target that internalizes NJsonSchema + Namotion.Reflection

**Files:**

- Create: `src/HotRepl.Core/ILRepack.targets`

- [ ] **Step 1: Write the target**

Create `src/HotRepl.Core/ILRepack.targets`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project>
  <!--
    Merge NJsonSchema and Namotion.Reflection into HotRepl.Core.dll
    so consumers see exactly one Core DLL plus Newtonsoft.Json
    (which is consumer-facing and stays external).

    Microsoft.CSharp is also a Namotion.Reflection transitive dep on
    netstandard2.x, but it's part of the Mono/Unity BCL so it resolves
    from the game runtime at load time — we deliberately do NOT
    internalize it and we DO list it in InternalizeExclude.
  -->
  <Target Name="ILRepackCore" AfterTargets="Build">
    <PropertyGroup>
      <CoreOutput>$(OutputPath)HotRepl.Core.dll</CoreOutput>
    </PropertyGroup>
    <ItemGroup>
      <ILRepackInput Include="$(CoreOutput)" />
      <ILRepackInput Include="$(OutputPath)NJsonSchema.dll" />
      <ILRepackInput Include="$(OutputPath)Namotion.Reflection.dll" />
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
    <!-- Remove side-by-side input DLLs so they don't ship from consumer
         deploy scripts that copy "everything in bin". -->
    <Delete Files="$(OutputPath)NJsonSchema.dll;$(OutputPath)Namotion.Reflection.dll" />
  </Target>
</Project>
```

- [ ] **Step 2: Import the target from the csproj**

Add a one-line `<Import>` to `src/HotRepl.Core/HotRepl.Core.csproj`, anywhere at the top level of
the `<Project>`:

```xml
<Import Project="ILRepack.targets" />
```

- [ ] **Step 3: Build and inspect output**

```bash
dotnet build src/HotRepl.Core/ --nologo -v q -c Release
ls src/HotRepl.Core/bin/Release/netstandard2.1/
```

Expected: directory contains `HotRepl.Core.dll` and `Newtonsoft.Json.dll` but NOT `NJsonSchema.dll`
or `Namotion.Reflection.dll`.

- [ ] **Step 4: Confirm merged Core contains internalized NJsonSchema types**

```bash
# dotnet ildasm is available on macOS via dotnet tool; if not installed:
#   dotnet tool install -g dotnet-ildasm
dotnet-ildasm src/HotRepl.Core/bin/Release/netstandard2.1/HotRepl.Core.dll \
  | grep -iE "njsonschema|namotion" | head -5
```

Expected: matches for internalized types (they'll have `private` or `assembly` access). If
`dotnet-ildasm` isn't available, skip — Step 3's file inventory is sufficient evidence.

- [ ] **Step 5: Commit**

```bash
git add src/HotRepl.Core/ILRepack.targets src/HotRepl.Core/HotRepl.Core.csproj
git commit -m "build(core): ILRepack NJsonSchema + Namotion.Reflection into HotRepl.Core.dll"
```

---

### A3: `EmptyArgs` marker struct

**Files:** Create `src/HotRepl.Core/Control/EmptyArgs.cs`

- [ ] **Step 1: Write the type**

```csharp
namespace HotRepl.Control;

/// <summary>
/// Marker type for typed commands that take no arguments. The schema
/// generator emits <c>{ "type": "object", "additionalProperties": false }</c>.
/// </summary>
public readonly struct EmptyArgs
{
}
```

- [ ] **Step 2: Commit**

```bash
git add src/HotRepl.Core/Control/EmptyArgs.cs
git commit -m "feat(core): add EmptyArgs marker struct for no-args typed commands"
```

---

### A4: `ControlCommandDiagnostic` + diagnostic kind

**Files:** Create `src/HotRepl.Core/Control/ControlCommandDiagnostic.cs`

- [ ] **Step 1: Write the types**

```csharp
namespace HotRepl.Control;

/// <summary>
/// Diagnostic carried in a typed control-command result. Failure
/// diagnostics drive the wire <c>status = "failed"</c> shape;
/// informational diagnostics ride alongside a successful result.
/// </summary>
public sealed record ControlCommandDiagnostic(
    ControlCommandDiagnosticKind Kind,
    string Code,
    string Message,
    bool Retryable = false,
    object? Details = null
);

/// <summary>Stable diagnostic taxonomy.</summary>
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

- [ ] **Step 2: Commit**

```bash
git add src/HotRepl.Core/Control/ControlCommandDiagnostic.cs
git commit -m "feat(core): add ControlCommandDiagnostic and diagnostic kinds"
```

---

### A5: `IArtifactWriter` interface

**Files:** Create `src/HotRepl.Core/Control/Artifacts/IArtifactWriter.cs`

- [ ] **Step 1: Write the interface**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.Control.Artifacts;

/// <summary>
/// Writes binary artifacts produced by a control-command handler.
/// Implementations live in <c>HotRepl.Core</c> and route to the host's
/// configured artifact store (typically a temp directory under the
/// game's plugin folder). Handlers retrieve the writer via
/// <see cref="ControlCommandContext.Artifacts"/>; the returned
/// <see cref="ArtifactRef"/> goes into
/// <see cref="ControlCommandResult{TOutput}.Artifacts"/>.
/// </summary>
/// <remarks>
/// Two writes with the same <c>logicalName</c> within one handler
/// invocation: the second replaces the first.
/// </remarks>
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

- [ ] **Step 2: Commit**

```bash
git add src/HotRepl.Core/Control/Artifacts/IArtifactWriter.cs
git commit -m "feat(core): add IArtifactWriter for typed-command artifact production"
```

---

### A6: Default in-memory `IArtifactWriter`

**Files:** Create `src/HotRepl.Core/Control/Artifacts/InMemoryArtifactWriter.cs`

- [ ] **Step 1: Write the test**

Create `tests/HotRepl.Tests/Unit/InMemoryArtifactWriterTests.cs`:

```csharp
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using HotRepl.Control.Artifacts;
using Xunit;

namespace HotRepl.Tests.Unit;

public class InMemoryArtifactWriterTests
{
    [Fact]
    public async Task WriteAsync_ProducesArtifactRefWithCorrectHash()
    {
        var writer = new InMemoryArtifactWriter();
        var bytes = Encoding.UTF8.GetBytes("hello world");

        var artifact = await writer.WriteAsync("greeting", bytes, "text/plain");

        Assert.Equal("greeting", artifact.LogicalName);
        Assert.Equal(bytes.Length, artifact.ByteSize);
        Assert.Equal("text/plain", artifact.ContentType);
        Assert.True(artifact.Finalized);
        // sha256("hello world")
        Assert.Equal(
            "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9",
            artifact.Sha256
        );
    }

    [Fact]
    public async Task WriteAsync_SameLogicalName_ReplacesPrevious()
    {
        var writer = new InMemoryArtifactWriter();
        await writer.WriteAsync("data", new byte[] { 1, 2, 3 });
        var second = await writer.WriteAsync("data", new byte[] { 4, 5, 6, 7 });

        Assert.Equal(4, second.ByteSize);
        Assert.Single(writer.Snapshot()); // exactly one entry
    }

    [Fact]
    public async Task WriteStreamAsync_ReadsToEndAndProducesRef()
    {
        var writer = new InMemoryArtifactWriter();
        var stream = new MemoryStream(new byte[] { 10, 20, 30 });

        var artifact = await writer.WriteStreamAsync("stream", stream);

        Assert.Equal(3, artifact.ByteSize);
    }
}
```

- [ ] **Step 2: Run failing test**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~InMemoryArtifactWriterTests
```

Expected: FAIL (type doesn't exist).

- [ ] **Step 3: Implement**

Create `src/HotRepl.Core/Control/Artifacts/InMemoryArtifactWriter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.Control.Artifacts;

/// <summary>
/// In-memory <see cref="IArtifactWriter"/>. Suitable for the default
/// command-dispatch path: artifacts live for the duration of the
/// containing command invocation (or job lifetime), then are
/// released when the writer is no longer reachable. Snapshot()
/// returns the current state for adapter projection.
/// </summary>
public sealed class InMemoryArtifactWriter : IArtifactWriter
{
    private readonly object _sync = new();
    private readonly Dictionary<string, StoredArtifact> _store = new(StringComparer.Ordinal);
    private readonly string _uriPrefix;

    public InMemoryArtifactWriter(string uriPrefix = "hotrepl-artifact://memory/")
    {
        _uriPrefix = uriPrefix;
    }

    public async ValueTask<ArtifactRef> WriteAsync(
        string logicalName,
        ReadOnlyMemory<byte> bytes,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(logicalName))
            throw new ArgumentException("Logical name required.", nameof(logicalName));
        cancellationToken.ThrowIfCancellationRequested();

        var copy = bytes.ToArray();
        var sha = Sha256Hex(copy);
        var artifact = new ArtifactRef(
            LogicalName: logicalName,
            Uri: _uriPrefix + logicalName,
            Path: null,
            ContentType: contentType,
            ByteSize: copy.Length,
            Sha256: sha,
            Finalized: true
        );

        lock (_sync) _store[logicalName] = new StoredArtifact(artifact, copy);
        return artifact;
    }

    public async ValueTask<ArtifactRef> WriteStreamAsync(
        string logicalName,
        Stream stream,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    )
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, 81920, cancellationToken).ConfigureAwait(false);
        return await WriteAsync(logicalName, ms.ToArray(), contentType, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Snapshot of all artifacts written so far. Used by the adapter to project into the wire shape.</summary>
    public IReadOnlyCollection<ArtifactRef> Snapshot()
    {
        lock (_sync)
        {
            var copy = new ArtifactRef[_store.Count];
            int i = 0;
            foreach (var v in _store.Values) copy[i++] = v.Ref;
            return copy;
        }
    }

    /// <summary>Bytes for a given logical name, or null if not present.</summary>
    public byte[]? GetBytes(string logicalName)
    {
        lock (_sync)
            return _store.TryGetValue(logicalName, out var stored) ? stored.Bytes : null;
    }

    private static string Sha256Hex(byte[] bytes)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        var sb = new System.Text.StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private sealed record StoredArtifact(ArtifactRef Ref, byte[] Bytes);
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~InMemoryArtifactWriterTests
```

Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/HotRepl.Core/Control/Artifacts/InMemoryArtifactWriter.cs tests/HotRepl.Tests/Unit/InMemoryArtifactWriterTests.cs
git commit -m "feat(core): in-memory IArtifactWriter for typed command results"
```

---

### A7: New `ControlCommandResult<TOutput>` + ergonomic factories

**Files:** Modify `src/HotRepl.Core/Control/ControlCommandResult.cs`

- [ ] **Step 1: Write the tests**

Create `tests/HotRepl.Tests/Unit/ControlCommandResultTests.cs`:

```csharp
using HotRepl.Control;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ControlCommandResultTests
{
    private sealed class Output
    {
        public int Value { get; init; }
    }

    [Fact]
    public void Ok_SetsSucceededAndOutput()
    {
        var r = ControlCommandResult<Output>.Ok(new Output { Value = 42 });
        Assert.True(r.Succeeded);
        Assert.Equal(42, r.Output!.Value);
        Assert.Empty(r.Artifacts);
        Assert.Empty(r.Diagnostics);
    }

    [Fact]
    public void ValidationFailed_SetsFailedAndDiagnostic()
    {
        var r = ControlCommandResult<Output>.ValidationFailed("badField", "Field X is required.");
        Assert.False(r.Succeeded);
        Assert.Null(r.Output);
        var d = Assert.Single(r.Diagnostics);
        Assert.Equal(ControlCommandDiagnosticKind.ValidationFailed, d.Kind);
        Assert.Equal("badField", d.Code);
    }

    [Fact]
    public void PreconditionFailed_SetsFailedAndDiagnostic()
    {
        var r = ControlCommandResult<Output>.PreconditionFailed("notReady", "Player not in world.");
        Assert.False(r.Succeeded);
        Assert.Equal(ControlCommandDiagnosticKind.PreconditionFailed, r.Diagnostics[0].Kind);
    }
}
```

- [ ] **Step 2: Run failing tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~ControlCommandResultTests
```

Expected: FAIL (type doesn't exist yet in new shape).

- [ ] **Step 3: Replace the existing non-generic shape**

The existing `src/HotRepl.Core/Control/ControlCommandResult.cs` defines a non-generic
`ControlCommandResult` record. **Replace** its contents:

```csharp
using System;
using System.Collections.Generic;
using HotRepl.Control.Artifacts;

namespace HotRepl.Control;

/// <summary>
/// Result returned by a typed control-command handler.
/// </summary>
public sealed class ControlCommandResult<TOutput>
{
    private static readonly IReadOnlyDictionary<string, ArtifactRef> EmptyArtifacts
        = new Dictionary<string, ArtifactRef>(0, StringComparer.Ordinal);

    private static readonly IReadOnlyList<ControlCommandDiagnostic> EmptyDiagnostics
        = Array.Empty<ControlCommandDiagnostic>();

    public TOutput? Output { get; init; }

    public IReadOnlyDictionary<string, ArtifactRef> Artifacts { get; init; } = EmptyArtifacts;

    public IReadOnlyList<ControlCommandDiagnostic> Diagnostics { get; init; } = EmptyDiagnostics;

    public bool Succeeded { get; init; } = true;

    // ---- factories ----

    public static ControlCommandResult<TOutput> Ok(TOutput output)
        => new() { Output = output };

    public static ControlCommandResult<TOutput> Ok(
        TOutput output,
        string artifactName,
        ArtifactRef artifact
    ) => new()
    {
        Output = output,
        Artifacts = new Dictionary<string, ArtifactRef>(StringComparer.Ordinal)
        {
            [artifactName] = artifact,
        },
    };

    public static ControlCommandResult<TOutput> Ok(
        TOutput output,
        IReadOnlyDictionary<string, ArtifactRef> artifacts
    ) => new()
    {
        Output = output,
        Artifacts = artifacts,
    };

    public static ControlCommandResult<TOutput> ValidationFailed(
        string code,
        string message,
        object? details = null
    ) => Failed(new ControlCommandDiagnostic(
        ControlCommandDiagnosticKind.ValidationFailed,
        code,
        message,
        Retryable: false,
        Details: details
    ));

    public static ControlCommandResult<TOutput> PreconditionFailed(
        string code,
        string message,
        object? details = null
    ) => Failed(new ControlCommandDiagnostic(
        ControlCommandDiagnosticKind.PreconditionFailed,
        code,
        message,
        Retryable: false,
        Details: details
    ));

    public static ControlCommandResult<TOutput> Failed(ControlCommandDiagnostic diagnostic)
        => new()
        {
            Succeeded = false,
            Diagnostics = new[] { diagnostic },
        };
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~ControlCommandResultTests
```

Expected: all PASS. The wider Core build will be broken because the router and other callers still
expect the old non-generic shape — that's fine, A14 fixes those. Don't run the wider build yet.

- [ ] **Step 5: Commit**

```bash
git add src/HotRepl.Core/Control/ControlCommandResult.cs tests/HotRepl.Tests/Unit/ControlCommandResultTests.cs
git commit -m "feat(core): replace non-generic ControlCommandResult with typed ControlCommandResult<TOutput>"
```

The wider Core build is intentionally broken at this point. Subsequent tasks repair it.

---

### A8: `IControlCommandHandler<TArgs, TOutput>` + delete old non-generic interface

**Files:** Replace `src/HotRepl.Core/Control/IControlCommandHandler.cs`

- [ ] **Step 1: Replace the file**

```csharp
using System.Threading;
using System.Threading.Tasks;

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
///     POCO output type. Same attribute rules apply; the schema
///     surfaces to clients via <c>command_describe</c>.
/// </typeparam>
public interface IControlCommandHandler<TArgs, TOutput>
{
    /// <summary>Stable wire name (e.g. <c>"compendium.info"</c>).</summary>
    string Name { get; }

    /// <summary>
    ///     Wire-protocol major version. Bump when args or output shape
    ///     changes incompatibly.
    /// </summary>
    int Version { get; }

    /// <summary>Synchronous (in-band response) or Job (out-of-band).</summary>
    ControlCommandKind Kind { get; }

    /// <summary>
    ///     True if this command may change game/runtime state. Used by
    ///     MCP to set the <c>destructiveHint</c> tool annotation.
    /// </summary>
    bool MutatesState { get; }

    /// <summary>
    ///     Execute the command. Invoked on the host's main-thread
    ///     execution path (via <c>ReplEngine.Tick()</c>). Continuations
    ///     after <c>await</c> resume on the Unity main thread via
    ///     <see cref="System.Threading.SynchronizationContext"/>.
    /// </summary>
    ValueTask<ControlCommandResult<TOutput>> ExecuteAsync(
        ControlCommandContext context,
        TArgs args,
        CancellationToken cancellationToken
    );
}
```

The previous non-generic `IControlCommandHandler` interface is gone. Callers that still reference it
(router, registry) will fail to compile — those are fixed in subsequent tasks.

- [ ] **Step 2: Commit**

```bash
git add src/HotRepl.Core/Control/IControlCommandHandler.cs
git commit -m "feat(core)!: replace IControlCommandHandler with typed IControlCommandHandler<TArgs, TOutput>"
```

The `!` in the commit type marks a breaking change.

---

### A9: New `ControlCommandContext` with progress + artifacts

**Files:** Replace `src/HotRepl.Core/Control/ControlCommandContext.cs`

- [ ] **Step 1: Add `ControlCommandProgress` record (same file)**

```csharp
using System;
using HotRepl.Control.Artifacts;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control;

/// <summary>Progress payload for a job command.</summary>
public sealed record ControlCommandProgress(
    JObject? Snapshot = null,
    string? Message = null
);
```

- [ ] **Step 2: Replace `ControlCommandContext`**

In the same file, replace the existing `ControlCommandContext` record:

```csharp
/// <summary>Per-invocation context handed to a typed command handler.</summary>
public sealed class ControlCommandContext
{
    public ControlCommandContext(
        string requestId,
        TimeSpan? timeout,
        string? jobId,
        IProgress<ControlCommandProgress> progress,
        IArtifactWriter artifacts
    )
    {
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        Timeout = timeout;
        JobId = jobId;
        Progress = progress ?? throw new ArgumentNullException(nameof(progress));
        Artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    }

    /// <summary>Originating wire request ID. Use for log correlation only.</summary>
    public string RequestId { get; }

    /// <summary>Caller-requested timeout. Null means no explicit caller timeout.</summary>
    public TimeSpan? Timeout { get; }

    /// <summary>Job ID, if this command is a job. Null for synchronous commands.</summary>
    public string? JobId { get; }

    /// <summary>
    ///     Progress sink. For synchronous commands, Report() calls are
    ///     silently dropped. For job commands, each call becomes a
    ///     <c>job_status_result</c> snapshot and an event in the job
    ///     event buffer.
    /// </summary>
    public IProgress<ControlCommandProgress> Progress { get; }

    /// <summary>
    ///     Artifact writer. Calls are idempotent on logical name
    ///     (a second write with the same name replaces the first).
    /// </summary>
    public IArtifactWriter Artifacts { get; }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/HotRepl.Core/Control/ControlCommandContext.cs
git commit -m "feat(core): expose Progress and Artifacts on ControlCommandContext"
```

---

### A10: Internal `ICompiledControlCommand` + supporting types

**Files:**

- Create: `src/HotRepl.Core/Control/Internal/ICompiledControlCommand.cs`
- Create: `src/HotRepl.Core/Control/Internal/CompiledCommandResult.cs`
- Create: `src/HotRepl.Core/Control/Internal/CompiledCommandContext.cs`
- Create: `src/HotRepl.Core/Control/Internal/SilentProgress.cs`
- Create: `src/HotRepl.Core/Control/Internal/ProgressSinkAdapter.cs`

- [ ] **Step 1: `ICompiledControlCommand`**

`src/HotRepl.Core/Control/Internal/ICompiledControlCommand.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Internal;

/// <summary>
/// Internal dispatch shape consumed by the router. Typed handlers are
/// wrapped in <see cref="TypedCommandAdapter{TArgs, TOutput}"/> to
/// satisfy this interface. Not exposed publicly — consumers author
/// against <see cref="IControlCommandHandler{TArgs, TOutput}"/>.
/// </summary>
internal interface ICompiledControlCommand
{
    ControlCommandDescriptor Descriptor { get; }

    ValueTask<CompiledCommandResult> ExecuteAsync(
        CompiledCommandContext context,
        JObject args,
        CancellationToken cancellationToken
    );
}
```

- [ ] **Step 2: `CompiledCommandResult`**

`src/HotRepl.Core/Control/Internal/CompiledCommandResult.cs`:

```csharp
using System;
using System.Collections.Generic;
using HotRepl.Control.Artifacts;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Internal;

/// <summary>
/// Internal record consumed by the router. Mirrors the wire shape:
/// status, output, top-level artifact list, top-level diagnostic list.
/// </summary>
internal sealed record CompiledCommandResult(
    bool Succeeded,
    JObject Output,
    IReadOnlyList<ArtifactRef> Artifacts,
    IReadOnlyList<ControlCommandError> Diagnostics
)
{
    public static CompiledCommandResult Empty { get; } = new(
        Succeeded: true,
        Output: new JObject(),
        Artifacts: Array.Empty<ArtifactRef>(),
        Diagnostics: Array.Empty<ControlCommandError>()
    );
}
```

- [ ] **Step 3: `CompiledCommandContext`**

`src/HotRepl.Core/Control/Internal/CompiledCommandContext.cs`:

```csharp
using System;
using HotRepl.Control.Artifacts;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Internal;

/// <summary>
/// Internal context handed to ICompiledControlCommand handlers. Carries
/// the raw progress callback the job manager wants and the artifact
/// writer instance the adapter will project into the result.
/// </summary>
internal sealed class CompiledCommandContext
{
    public CompiledCommandContext(
        string requestId,
        TimeSpan? timeout,
        string? jobId,
        Action<JObject?, string?>? progressSink,
        IArtifactWriter artifacts
    )
    {
        RequestId = requestId;
        Timeout = timeout;
        JobId = jobId;
        ProgressSink = progressSink;
        Artifacts = artifacts;
    }

    public string RequestId { get; }
    public TimeSpan? Timeout { get; }
    public string? JobId { get; }
    /// <summary>Null for synchronous commands; non-null for jobs.</summary>
    public Action<JObject?, string?>? ProgressSink { get; }
    public IArtifactWriter Artifacts { get; }
}
```

- [ ] **Step 4: `SilentProgress`**

`src/HotRepl.Core/Control/Internal/SilentProgress.cs`:

```csharp
using System;

namespace HotRepl.Control.Internal;

/// <summary>No-op IProgress for synchronous commands.</summary>
internal sealed class SilentProgress : IProgress<ControlCommandProgress>
{
    public static readonly SilentProgress Instance = new();

    private SilentProgress() { }

    public void Report(ControlCommandProgress value) { }
}
```

- [ ] **Step 5: `ProgressSinkAdapter`**

`src/HotRepl.Core/Control/Internal/ProgressSinkAdapter.cs`:

```csharp
using System;

namespace HotRepl.Control.Internal;

/// <summary>
/// Bridges the job manager's <c>Action&lt;JObject?, string?&gt;</c>
/// progress callback to <c>IProgress&lt;ControlCommandProgress&gt;</c>
/// for handlers.
/// </summary>
internal sealed class ProgressSinkAdapter : IProgress<ControlCommandProgress>
{
    private readonly Action<Newtonsoft.Json.Linq.JObject?, string?> _sink;

    public ProgressSinkAdapter(Action<Newtonsoft.Json.Linq.JObject?, string?> sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public void Report(ControlCommandProgress value)
    {
        _sink(value.Snapshot, value.Message);
    }
}
```

- [ ] **Step 6: Commit**

```bash
git add src/HotRepl.Core/Control/Internal/
git commit -m "feat(core): add internal compiled-command dispatch types"
```

---

### A11: Schema cache (NJsonSchema-backed)

**Files:** Create `src/HotRepl.Core/Control/Schema/SchemaCache.cs`

- [ ] **Step 1: Write the tests**

Create `tests/HotRepl.Tests/Unit/SchemaCacheTests.cs`:

```csharp
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HotRepl.Control;
using Xunit;

namespace HotRepl.Tests.Unit;

public class SchemaCacheTests
{
    private sealed class TestArgs
    {
        [Required, Range(0, 100)]
        [Description("0-100 inclusive.")]
        public int Value { get; set; }

        public string? OptionalNote { get; set; }
    }

    [Fact]
    public void EmptyArgs_EmitsAdditionalPropertiesFalseSchema()
    {
        var schema = SchemaCache.For<EmptyArgs>();

        Assert.Equal("object", (string?)schema["type"]);
        Assert.False((bool)schema["additionalProperties"]!);
    }

    [Fact]
    public void TypedArgs_HonorsRequiredRangeAndDescription()
    {
        var schema = SchemaCache.For<TestArgs>();

        Assert.Equal("object", (string?)schema["type"]);
        Assert.NotNull(schema["properties"]);

        var value = schema["properties"]!["Value"]!;
        Assert.Equal("integer", (string?)value["type"]);
        Assert.Equal(0, (int)value["minimum"]!);
        Assert.Equal(100, (int)value["maximum"]!);
        Assert.Equal("0-100 inclusive.", (string?)value["description"]);

        var required = (IList<Newtonsoft.Json.Linq.JToken>)schema["required"]!;
        Assert.Contains("Value", required.Select(t => (string?)t));
    }

    [Fact]
    public void For_CachesSchemaPerType()
    {
        var a = SchemaCache.For<TestArgs>();
        var b = SchemaCache.For<TestArgs>();
        Assert.Same(a, b);
    }

    [Fact]
    public void AnyObject_IsConstant()
    {
        Assert.Equal("object", (string?)SchemaCache.AnyObject["type"]);
        Assert.True((bool)SchemaCache.AnyObject["additionalProperties"]!);
    }
}
```

- [ ] **Step 2: Run failing tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~SchemaCacheTests
```

Expected: FAIL (type doesn't exist).

- [ ] **Step 3: Implement `SchemaCache`**

`src/HotRepl.Core/Control/Schema/SchemaCache.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Schema;

/// <summary>
/// JSON schema cache keyed by .NET type. Backed by NJsonSchema 10.9.0
/// (ILRepack-internalized in HotRepl.Core.dll). Schemas are pre-warmed
/// at adapter construction so the first agent request never pays for
/// reflection.
/// </summary>
public static class SchemaCache
{
    private static readonly ConcurrentDictionary<Type, JObject> Cache = new();

    /// <summary>
    /// Open-object schema with <c>additionalProperties: true</c>; the
    /// universal artifacts-schema fallback.
    /// </summary>
    public static JObject AnyObject { get; } = JObject.Parse(
        "{ \"type\": \"object\", \"additionalProperties\": true }"
    );

    /// <summary>
    /// Closed-object schema with <c>additionalProperties: false</c>;
    /// the EmptyArgs schema.
    /// </summary>
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
            DefaultReferenceTypeNullHandling =
                NJsonSchema.Generation.ReferenceTypeNullHandling.Null,
            AllowReferencesWithProperties = false, // inline; no $ref
        };
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~SchemaCacheTests
```

Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/HotRepl.Core/Control/Schema/SchemaCache.cs tests/HotRepl.Tests/Unit/SchemaCacheTests.cs
git commit -m "feat(core): NJsonSchema-backed schema cache for typed handlers"
```

---

### A12: `IControlCommandValidator` + NJsonSchema-backed default

**Files:**

- Create: `src/HotRepl.Core/Control/Schema/IControlCommandValidator.cs`
- Create: `src/HotRepl.Core/Control/Schema/NJsonSchemaValidator.cs`

- [ ] **Step 1: Tests**

`tests/HotRepl.Tests/Unit/ControlCommandValidatorTests.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using HotRepl.Control;
using HotRepl.Control.Schema;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ControlCommandValidatorTests
{
    private sealed class Args
    {
        [Required, Range(0, 100)]
        public int Value { get; set; }
    }

    private static readonly NJsonSchemaValidator Validator = new();
    private static readonly JObject ArgsSchema = SchemaCache.For<Args>();

    [Fact]
    public void Validate_PassesValidArgs()
    {
        var result = Validator.Validate(JObject.Parse("{\"Value\": 42}"), ArgsSchema);
        Assert.True(result.Ok);
    }

    [Fact]
    public void Validate_FailsOnRangeViolation()
    {
        var result = Validator.Validate(JObject.Parse("{\"Value\": 200}"), ArgsSchema);
        Assert.False(result.Ok);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_FailsOnMissingRequired()
    {
        var result = Validator.Validate(JObject.Parse("{}"), ArgsSchema);
        Assert.False(result.Ok);
    }

    [Fact]
    public void Validate_PassesEmptyArgsAgainstClosedSchema()
    {
        var schema = SchemaCache.For<EmptyArgs>();
        var result = Validator.Validate(JObject.Parse("{}"), schema);
        Assert.True(result.Ok);
    }
}
```

- [ ] **Step 2: Run failing tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~ControlCommandValidatorTests
```

Expected: FAIL.

- [ ] **Step 3: Implement the interface**

`src/HotRepl.Core/Control/Schema/IControlCommandValidator.cs`:

```csharp
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Schema;

/// <summary>Validates command arguments against a JSON schema.</summary>
public interface IControlCommandValidator
{
    SchemaValidationResult Validate(JObject args, JObject schema);
}

/// <summary>Outcome of a schema validation.</summary>
public readonly struct SchemaValidationResult
{
    public SchemaValidationResult(bool ok, IReadOnlyList<string> errors)
    {
        Ok = ok;
        Errors = errors;
    }

    public bool Ok { get; }
    public IReadOnlyList<string> Errors { get; }

    public static SchemaValidationResult Pass { get; } =
        new(true, System.Array.Empty<string>());

    public ControlCommandError ToDiagnostic()
    {
        var first = Errors.Count > 0 ? Errors[0] : "Argument schema validation failed.";
        var details = new JObject { ["errors"] = JArray.FromObject(Errors) };
        return new ControlCommandError(
            Kind: "validation_failed",
            Code: "argsSchemaViolation",
            Message: first,
            Retryable: false,
            Details: details
        );
    }
}
```

- [ ] **Step 4: Implement the NJsonSchema-backed validator**

`src/HotRepl.Core/Control/Schema/NJsonSchemaValidator.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Schema;

/// <summary>
/// Default <see cref="IControlCommandValidator"/>. Internalized
/// NJsonSchema parses the schema and validates the args. Validation
/// errors are projected to strings for the wire-shape diagnostic.
/// </summary>
internal sealed class NJsonSchemaValidator : IControlCommandValidator
{
    public SchemaValidationResult Validate(JObject args, JObject schema)
    {
        var parsed = NJsonSchema.JsonSchema.FromJsonAsync(schema.ToString())
            .GetAwaiter().GetResult();
        var errors = parsed.Validate(args);
        if (errors.Count == 0) return SchemaValidationResult.Pass;

        var formatted = errors
            .Select(e => e.Path is null or "" ? e.Kind.ToString() : $"{e.Path}: {e.Kind}")
            .ToArray();
        return new SchemaValidationResult(false, formatted);
    }
}
```

- [ ] **Step 5: Mark `NJsonSchemaValidator` accessible in tests**

The test class above is in `HotRepl.Tests` which has `InternalsVisibleTo`, so it can
`new NJsonSchemaValidator()`. Run:

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~ControlCommandValidatorTests
```

Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/HotRepl.Core/Control/Schema/IControlCommandValidator.cs src/HotRepl.Core/Control/Schema/NJsonSchemaValidator.cs tests/HotRepl.Tests/Unit/ControlCommandValidatorTests.cs
git commit -m "feat(core): NJsonSchema-backed validator with diagnostic-projection"
```

---

### A13: `TypedCommandAdapter<TArgs, TOutput>`

**Files:** Create `src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs`

- [ ] **Step 1: Tests**

`tests/HotRepl.Tests/Unit/TypedCommandAdapterTests.cs`:

```csharp
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.Control.Artifacts;
using HotRepl.Control.Internal;
using HotRepl.Control.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

public class TypedCommandAdapterTests
{
    private sealed class GreetArgs
    {
        [Required]
        public string Name { get; set; } = "";
    }

    private sealed class GreetResult
    {
        public string Greeting { get; set; } = "";
    }

    private sealed class GreetCommand : IControlCommandHandler<GreetArgs, GreetResult>
    {
        public string Name => "test.greet";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Synchronous;
        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<GreetResult>> ExecuteAsync(
            ControlCommandContext _, GreetArgs args, CancellationToken __)
            => new(ControlCommandResult<GreetResult>.Ok(new GreetResult
            {
                Greeting = $"Hello, {args.Name}!",
            }));
    }

    private static readonly JsonSerializer Serializer = JsonSerializer.CreateDefault();
    private static readonly NJsonSchemaValidator Validator = new();

    private static CompiledCommandContext NewContext() =>
        new(
            requestId: "req-1",
            timeout: null,
            jobId: null,
            progressSink: null,
            artifacts: new InMemoryArtifactWriter()
        );

    [Fact]
    public async Task ValidArgs_FlowsThroughHandlerAndReturnsOutput()
    {
        var adapter = new TypedCommandAdapter<GreetArgs, GreetResult>(
            new GreetCommand(), Serializer, Validator);

        var result = await adapter.ExecuteAsync(
            NewContext(),
            JObject.Parse("{\"Name\":\"World\"}"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Hello, World!", (string?)result.Output["Greeting"]);
    }

    [Fact]
    public async Task InvalidArgs_FailValidationWithoutCallingHandler()
    {
        bool called = false;
        var handler = new TrackingCommand(_ => called = true);
        var adapter = new TypedCommandAdapter<GreetArgs, GreetResult>(
            handler, Serializer, Validator);

        var result = await adapter.ExecuteAsync(
            NewContext(),
            JObject.Parse("{}"), // missing required Name
            CancellationToken.None);

        Assert.False(called);
        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Equal("validation_failed", result.Diagnostics[0].Kind);
    }

    [Fact]
    public async Task HandlerArtifacts_ProjectIntoTopLevelList()
    {
        var adapter = new TypedCommandAdapter<EmptyArgs, GreetResult>(
            new ArtifactProducingCommand(), Serializer, Validator);

        var result = await adapter.ExecuteAsync(
            NewContext(),
            new JObject(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Artifacts);
        Assert.Equal("manifest", result.Artifacts[0].LogicalName);
    }

    [Fact]
    public async Task PreconditionFailed_BecomesDiagnostic()
    {
        var adapter = new TypedCommandAdapter<EmptyArgs, GreetResult>(
            new FailingCommand(), Serializer, Validator);

        var result = await adapter.ExecuteAsync(
            NewContext(),
            new JObject(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("precondition_failed", result.Diagnostics[0].Kind);
    }

    private sealed class TrackingCommand : IControlCommandHandler<GreetArgs, GreetResult>
    {
        private readonly Action<GreetArgs> _onCall;
        public TrackingCommand(Action<GreetArgs> onCall) { _onCall = onCall; }
        public string Name => "test.tracking";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Synchronous;
        public bool MutatesState => false;
        public ValueTask<ControlCommandResult<GreetResult>> ExecuteAsync(
            ControlCommandContext _, GreetArgs args, CancellationToken __)
        {
            _onCall(args);
            return new(ControlCommandResult<GreetResult>.Ok(new GreetResult()));
        }
    }

    private sealed class ArtifactProducingCommand : IControlCommandHandler<EmptyArgs, GreetResult>
    {
        public string Name => "test.artifact";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Synchronous;
        public bool MutatesState => false;
        public async ValueTask<ControlCommandResult<GreetResult>> ExecuteAsync(
            ControlCommandContext context, EmptyArgs _, CancellationToken __)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes("manifest content");
            var artifact = await context.Artifacts.WriteAsync("manifest", bytes, "text/plain");
            return ControlCommandResult<GreetResult>.Ok(
                new GreetResult(),
                "manifest",
                artifact
            );
        }
    }

    private sealed class FailingCommand : IControlCommandHandler<EmptyArgs, GreetResult>
    {
        public string Name => "test.failing";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Synchronous;
        public bool MutatesState => false;
        public ValueTask<ControlCommandResult<GreetResult>> ExecuteAsync(
            ControlCommandContext _, EmptyArgs __, CancellationToken ___)
            => new(ControlCommandResult<GreetResult>.PreconditionFailed("notReady", "Not ready."));
    }
}
```

- [ ] **Step 2: Run failing tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~TypedCommandAdapterTests
```

Expected: FAIL (`TypedCommandAdapter` doesn't exist).

- [ ] **Step 3: Implement the adapter**

`src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control.Artifacts;
using HotRepl.Control.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Internal;

/// <summary>
/// Bridges a typed handler into the internal compiled-command shape the
/// router consumes. Validates args server-side, deserializes typed
/// args, runs the handler on the captured synchronization context, and
/// projects the typed result back to the wire shape.
/// </summary>
internal sealed class TypedCommandAdapter<TArgs, TOutput> : ICompiledControlCommand
{
    private readonly IControlCommandHandler<TArgs, TOutput> _inner;
    private readonly JsonSerializer _serializer;
    private readonly IControlCommandValidator _validator;

    public TypedCommandAdapter(
        IControlCommandHandler<TArgs, TOutput> inner,
        JsonSerializer serializer,
        IControlCommandValidator validator
    )
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
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

    public ControlCommandDescriptor Descriptor { get; }

    public async ValueTask<CompiledCommandResult> ExecuteAsync(
        CompiledCommandContext compiledContext,
        JObject args,
        CancellationToken ct
    )
    {
        // 1. Validate against the descriptor's args schema.
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

        // 2. Deserialize typed args (EmptyArgs is special-cased).
        TArgs typedArgs;
        if (typeof(TArgs) == typeof(EmptyArgs))
        {
            typedArgs = default!;
        }
        else
        {
            typedArgs = args.ToObject<TArgs>(_serializer)
                ?? throw new InvalidOperationException(
                    $"Newtonsoft deserialized {typeof(TArgs).Name} as null."
                );
        }

        // 3. Build the typed context.
        IProgress<ControlCommandProgress> progress =
            compiledContext.ProgressSink is null
                ? SilentProgress.Instance
                : new ProgressSinkAdapter(compiledContext.ProgressSink);

        var typedContext = new ControlCommandContext(
            requestId: compiledContext.RequestId,
            timeout: compiledContext.Timeout,
            jobId: compiledContext.JobId,
            progress: progress,
            artifacts: compiledContext.Artifacts
        );

        // 4. Run the handler. ConfigureAwait(true) keeps us on the
        //    Unity sync context for any continuation.
        var typedResult = await _inner
            .ExecuteAsync(typedContext, typedArgs, ct)
            .ConfigureAwait(true);

        // 5. Project the typed result to the wire shape.
        var outputJson = typedResult.Output is null
            ? new JObject()
            : JObject.FromObject(typedResult.Output, _serializer);

        var artifactList = typedResult.Artifacts.Values.ToArray();

        var diagnostics = typedResult.Diagnostics
            .Select(ToError)
            .ToArray();

        return new CompiledCommandResult(
            Succeeded: typedResult.Succeeded,
            Output: outputJson,
            Artifacts: artifactList,
            Diagnostics: diagnostics
        );
    }

    private static ControlCommandError ToError(ControlCommandDiagnostic diagnostic) =>
        new(
            Kind: DiagnosticKindToWire(diagnostic.Kind),
            Code: diagnostic.Code,
            Message: diagnostic.Message,
            Retryable: diagnostic.Retryable,
            Details: diagnostic.Details is null
                ? null
                : JObject.FromObject(diagnostic.Details)
        );

    private static string DiagnosticKindToWire(ControlCommandDiagnosticKind kind) => kind switch
    {
        ControlCommandDiagnosticKind.Info               => "info",
        ControlCommandDiagnosticKind.Warning            => "warning",
        ControlCommandDiagnosticKind.ValidationFailed   => "validation_failed",
        ControlCommandDiagnosticKind.PreconditionFailed => "precondition_failed",
        ControlCommandDiagnosticKind.Conflict           => "conflict",
        ControlCommandDiagnosticKind.Cancelled          => "cancelled",
        _                                               => "internal",
    };
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~TypedCommandAdapterTests
```

Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs tests/HotRepl.Tests/Unit/TypedCommandAdapterTests.cs
git commit -m "feat(core): TypedCommandAdapter projects typed handlers to the dispatch shape"
```

---

### A14: Registry: add `Register<TArgs, TOutput>`; switch `TryGet` to internal compiled shape

**Files:**

- Modify: `src/HotRepl.Core/Control/IControlCommandRegistry.cs`
- Modify: `src/HotRepl.Core/Control/GlobalControlCommandRegistry.cs`
- Modify: `src/HotRepl.Core/Control/EmptyControlCommandRegistry.cs`

- [ ] **Step 1: Tests**

`tests/HotRepl.Tests/Unit/RegistryTypedRegisterTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using Xunit;

namespace HotRepl.Tests.Unit;

public class RegistryTypedRegisterTests
{
    private sealed class Cmd : IControlCommandHandler<EmptyArgs, EmptyArgs>
    {
        public string Name => "test.cmd";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Synchronous;
        public bool MutatesState => false;
        public ValueTask<ControlCommandResult<EmptyArgs>> ExecuteAsync(
            ControlCommandContext _, EmptyArgs __, CancellationToken ___)
            => new(ControlCommandResult<EmptyArgs>.Ok(new EmptyArgs()));
    }

    [Fact]
    public void Register_ExposesTypedHandlerThroughDescribe()
    {
        var registry = new GlobalControlCommandRegistry();
        using var _ = registry.Register(new Cmd());

        var descriptors = registry.Describe();
        Assert.Single(descriptors);
        Assert.Equal("test.cmd", descriptors[0].Name);
    }

    [Fact]
    public void Register_DuplicateThrows()
    {
        var registry = new GlobalControlCommandRegistry();
        using var _ = registry.Register(new Cmd());

        Assert.Throws<InvalidOperationException>(() => registry.Register(new Cmd()));
    }

    [Fact]
    public void Dispose_UnregistersHandler()
    {
        var registry = new GlobalControlCommandRegistry();
        var registration = registry.Register(new Cmd());
        registration.Dispose();

        Assert.Empty(registry.Describe());
    }

    [Fact]
    public void TryGet_ReturnsTheCompiledHandler()
    {
        var registry = new GlobalControlCommandRegistry();
        using var _ = registry.Register(new Cmd());

        Assert.True(registry.TryGet("test.cmd", out var handler));
        Assert.NotNull(handler);
        Assert.Equal("test.cmd", handler!.Descriptor.Name);
    }
}
```

(`GlobalControlCommandRegistry`'s constructor is currently private — we'll widen it to internal for
this test, then keep the singleton `Instance` for production callers.)

- [ ] **Step 2: Run failing tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~RegistryTypedRegisterTests
```

Expected: FAIL.

- [ ] **Step 3: Update the interface**

Replace `src/HotRepl.Core/Control/IControlCommandRegistry.cs`:

```csharp
using System;
using System.Collections.Generic;
using HotRepl.Control.Internal;

namespace HotRepl.Control;

/// <summary>Registry of host-provided control-plane commands.</summary>
public interface IControlCommandRegistry
{
    /// <summary>Descriptors advertised to clients.</summary>
    IReadOnlyList<ControlCommandDescriptor> Describe();

    /// <summary>
    /// Register a typed command. The returned disposable unregisters on
    /// dispose; use it for proper teardown in plugin OnDestroy /
    /// OnDeinitializeMelon.
    /// </summary>
    IDisposable Register<TArgs, TOutput>(IControlCommandHandler<TArgs, TOutput> handler);

    /// <summary>
    /// Internal lookup used by the router. Returns the compiled
    /// dispatch shape, not the consumer-facing typed shape.
    /// </summary>
    bool TryGet(string name, out ICompiledControlCommand? handler);
}
```

- [ ] **Step 4: Update the global registry**

Replace `src/HotRepl.Core/Control/GlobalControlCommandRegistry.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using HotRepl.Control.Internal;
using HotRepl.Control.Schema;
using Newtonsoft.Json;

namespace HotRepl.Control;

/// <summary>
/// Process-wide registry used by loaded host/game plugins to expose
/// control commands. Typed handlers passed to <c>Register</c> are
/// wrapped in a <see cref="TypedCommandAdapter{TArgs,TOutput}"/>.
/// </summary>
public sealed class GlobalControlCommandRegistry : IControlCommandRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ICompiledControlCommand> _handlers =
        new(StringComparer.Ordinal);
    private readonly JsonSerializer _serializer;
    private readonly IControlCommandValidator _validator;

    /// <summary>Shared registry used by host adapters.</summary>
    public static GlobalControlCommandRegistry Instance { get; } = new();

    /// <summary>Public ctor for unit tests; production code uses <see cref="Instance"/>.</summary>
    public GlobalControlCommandRegistry()
        : this(JsonSerializer.CreateDefault(), new NJsonSchemaValidator()) { }

    internal GlobalControlCommandRegistry(
        JsonSerializer serializer,
        IControlCommandValidator validator
    )
    {
        _serializer = serializer;
        _validator = validator;
    }

    /// <inheritdoc />
    public IDisposable Register<TArgs, TOutput>(
        IControlCommandHandler<TArgs, TOutput> handler
    )
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        var compiled = new TypedCommandAdapter<TArgs, TOutput>(handler, _serializer, _validator);
        var name = compiled.Descriptor.Name;

        lock (_sync)
        {
            if (_handlers.ContainsKey(name))
                throw new InvalidOperationException(
                    $"Control command '{name}' is already registered."
                );
            _handlers.Add(name, compiled);
        }

        return new Registration(this, name, compiled);
    }

    /// <inheritdoc />
    public IReadOnlyList<ControlCommandDescriptor> Describe()
    {
        lock (_sync)
            return _handlers.Values
                .Select(h => h.Descriptor)
                .OrderBy(d => d.Name, StringComparer.Ordinal)
                .ToArray();
    }

    /// <inheritdoc />
    public bool TryGet(string name, out ICompiledControlCommand? handler)
    {
        lock (_sync)
            return _handlers.TryGetValue(name, out handler);
    }

    private void Unregister(string name, ICompiledControlCommand handler)
    {
        lock (_sync)
        {
            if (_handlers.TryGetValue(name, out var current) && ReferenceEquals(current, handler))
                _handlers.Remove(name);
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly GlobalControlCommandRegistry _owner;
        private readonly string _name;
        private readonly ICompiledControlCommand _handler;
        private bool _disposed;

        public Registration(GlobalControlCommandRegistry owner, string name, ICompiledControlCommand handler)
        {
            _owner = owner;
            _name = name;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.Unregister(_name, _handler);
        }
    }
}
```

- [ ] **Step 5: Update the empty registry**

Replace `src/HotRepl.Core/Control/EmptyControlCommandRegistry.cs`:

```csharp
using System;
using System.Collections.Generic;
using HotRepl.Control.Internal;

namespace HotRepl.Control;

/// <summary>No-op registry used when control commands are disabled.</summary>
internal sealed class EmptyControlCommandRegistry : IControlCommandRegistry
{
    public static EmptyControlCommandRegistry Instance { get; } = new();

    public IReadOnlyList<ControlCommandDescriptor> Describe()
        => Array.Empty<ControlCommandDescriptor>();

    public IDisposable Register<TArgs, TOutput>(
        IControlCommandHandler<TArgs, TOutput> handler
    ) => NullRegistration.Instance;

    public bool TryGet(string name, out ICompiledControlCommand? handler)
    {
        handler = null;
        return false;
    }

    private sealed class NullRegistration : IDisposable
    {
        public static NullRegistration Instance { get; } = new();
        public void Dispose() { }
    }
}
```

- [ ] **Step 6: Run tests**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~RegistryTypedRegisterTests
```

Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/HotRepl.Core/Control/IControlCommandRegistry.cs src/HotRepl.Core/Control/GlobalControlCommandRegistry.cs src/HotRepl.Core/Control/EmptyControlCommandRegistry.cs tests/HotRepl.Tests/Unit/RegistryTypedRegisterTests.cs
git commit -m "feat(core): registry registers typed handlers; TryGet returns compiled dispatch shape"
```

---

### A15: Router + job manager: consume `ICompiledControlCommand`, wire progress through

**Files:**

- Modify: `src/HotRepl.Core/Control/ControlCommandRouter.cs`
- Modify: `src/HotRepl.Core/Control/Jobs/ControlJobManager.cs`
- Delete: `src/HotRepl.Core/Control/Jobs/ControlJobExecutionContext.cs`

This is the biggest single edit. The router previously called
`IControlCommandHandler.ExecuteAsync(ControlCommandContext, JObject, ct)` and got back the old
non-generic `ControlCommandResult`. After this task it calls
`ICompiledControlCommand.ExecuteAsync(CompiledCommandContext,
JObject, ct)` and gets back the
internal `CompiledCommandResult`. The projection to wire messages is otherwise unchanged.

- [ ] **Step 1: Read the existing router and job manager carefully**

```bash
read src/HotRepl.Core/Control/ControlCommandRouter.cs:raw
read src/HotRepl.Core/Control/Jobs/ControlJobManager.cs:raw
```

Note the existing methods: `ExecuteSynchronous`, `StartJob`, the `IControlCommandHandler` typing on
`_registry.TryGet`, the construction of `ControlCommandContext`, and the use of
`ControlJobExecutionContext.ToCommandContext`.

- [ ] **Step 2: Update the router signatures**

In `ControlCommandRouter.cs`:

1. Change `if (!_registry.TryGet(message.Name, out var handler))` blocks to expect the new nullable
   `ICompiledControlCommand?`. Use null-check:
   `if (!_registry.TryGet(message.Name, out var handler) || handler is null)`.

2. In `ExecuteSynchronous`, replace the `ControlCommandContext` construction with a
   `CompiledCommandContext`:

```csharp
var requestedTimeout = message.TimeoutMs.GetValueOrDefault();
var timeout = requestedTimeout > 0
    ? TimeSpan.FromMilliseconds(requestedTimeout)
    : (TimeSpan?)null;

var compiledContext = new CompiledCommandContext(
    requestId: message.Id,
    timeout: timeout,
    jobId: null,
    progressSink: null, // sync commands have no progress sink
    artifacts: new InMemoryArtifactWriter()
);

var result = handler
    .ExecuteAsync(compiledContext, message.Args, CancellationToken.None)
    .AsTask().GetAwaiter().GetResult();
```

3. Change the result projection from `result.Result` to `result.Output`, and `result.Artifacts`
   (already an `IReadOnlyList<ArtifactRef>`), and use `result.Diagnostics`. Update the wire-shape
   `status` to come from `result.Succeeded ? "ok" : "failed"`.

4. In `StartJob`, change the inline lambda to use the new compiled-context shape; pass the progress
   sink through:

```csharp
var job = _jobs.StartJob(
    connectionId,
    message.Id,
    (jobCtx, token) =>
    {
        var compiledContext = new CompiledCommandContext(
            requestId: message.Id,
            timeout: timeout,
            jobId: jobCtx.JobId,
            progressSink: jobCtx.ProgressSink,
            artifacts: jobCtx.Artifacts
        );
        return handler.ExecuteAsync(compiledContext, message.Args, token);
    }
);
```

5. Wherever the old result was projected to a wire message, ensure the `status = "failed"` path is
   exercised when `result.Succeeded == false` and the top-level wire envelope picks up the
   diagnostics.

- [ ] **Step 3: Refactor the job manager**

Replace `ControlJobExecutionContext`'s sole purpose (handing the handler a sync-shaped context) with
a richer execution context the router can directly populate.

In `Jobs/ControlJobManager.cs`:

1. Change `Execute` field type from
   `Func<ControlJobExecutionContext, CancellationToken, ValueTask<ControlCommandResult>>` to
   `Func<JobExecutionEnvironment, CancellationToken, ValueTask<CompiledCommandResult>>`.

2. Define `JobExecutionEnvironment` as an internal struct in the same file:

   ```csharp
   internal readonly struct JobExecutionEnvironment
   {
       public JobExecutionEnvironment(
           string jobId,
           Action<JObject?, string?> progressSink,
           IArtifactWriter artifacts
       )
       {
           JobId = jobId;
           ProgressSink = progressSink;
           Artifacts = artifacts;
       }

       public string JobId { get; }
       public Action<JObject?, string?> ProgressSink { get; }
       public IArtifactWriter Artifacts { get; }
   }
   ```

3. In `RunAsync`, construct the `JobExecutionEnvironment` instead of `ControlJobExecutionContext`.
   Pass the artifact writer (a fresh `InMemoryArtifactWriter` per job).

4. Replace all references to `ControlCommandResult` (old non-generic) inside the job manager with
   `CompiledCommandResult`.

5. In the `JobState` class, replace the old fields (`Result`, `Artifacts`, `Diagnostics`) with the
   new shape (the existing storage of `JObject? Result` becomes `JObject? Output`, etc.).

6. The `GetStatus` method's projection to `ControlJobStatus` keeps the same wire shape — only the C#
   field types change.

- [ ] **Step 4: Delete `ControlJobExecutionContext.cs`**

```bash
git rm src/HotRepl.Core/Control/Jobs/ControlJobExecutionContext.cs
```

- [ ] **Step 5: Verify the broader Core build**

```bash
dotnet build src/HotRepl.Core/ --nologo -v q
```

Expected: clean build. Any test code in `HotRepl.Tests` that still references the old
`IControlCommandHandler` non-generic interface or the old `ControlCommandResult` non-generic record
fails to compile here — fix those in the next task.

- [ ] **Step 6: Update existing C# tests that touched the old shapes**

The following test files reference the old `IControlCommandHandler` / `ControlCommandResult` shapes
and need rewriting against the typed interface:

```bash
grep -rln "IControlCommandHandler\b\|ControlCommandResult\b" tests/HotRepl.Tests/
```

For each file the grep surfaces, rewrite to use the new typed shape. The migration recipe per file:

1. Replace any custom `IControlCommandHandler`-implementing test double with an
   `IControlCommandHandler<TArgs, TOutput>` typed shape.
2. Replace `new ControlCommandResult(json, artifacts, diagnostics)` with
   `ControlCommandResult<TOutput>.Ok(...)` or the appropriate factory.
3. Replace any direct `Register(IControlCommandHandler)` call with the typed
   `Register<TArgs, TOutput>(...)`.
4. Update any assertions that read `.Result` on the old non-generic result to read `.Output` on the
   typed wrapper (where the test constructs the typed result) or `.Output` on
   `CompiledCommandResult` (where the test reads through the adapter).

After the test rewrites:

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q
```

Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/HotRepl.Core/Control/ControlCommandRouter.cs src/HotRepl.Core/Control/Jobs/ tests/HotRepl.Tests/
git commit -m "feat(core)!: router and job manager consume compiled-command dispatch shape"
```

---

### A16: Integration test — typed handler end-to-end through the router

**Files:** Create `tests/HotRepl.Tests/Integration/TypedCommandRoundTripTests.cs`

- [ ] **Step 1: Write the test**

This is the integration test that validates the entire dispatch pipeline: register a typed handler →
router serializes the wire message → adapter validates + deserializes → handler runs → adapter
projects back → wire result.

```csharp
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Integration;

public class TypedCommandRoundTripTests
{
    // A typed command for round-trip testing.
    private sealed class EchoArgs
    {
        public string Message { get; set; } = "";
    }

    private sealed class EchoResult
    {
        public string Echoed { get; set; } = "";
    }

    private sealed class EchoCommand : IControlCommandHandler<EchoArgs, EchoResult>
    {
        public string Name => "test.echo";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Synchronous;
        public bool MutatesState => false;
        public ValueTask<ControlCommandResult<EchoResult>> ExecuteAsync(
            ControlCommandContext _, EchoArgs args, System.Threading.CancellationToken __)
            => new(ControlCommandResult<EchoResult>.Ok(new EchoResult
            {
                Echoed = args.Message,
            }));
    }

    [Fact]
    public void RegisterAndExecute_ProducesWireOkResult()
    {
        var registry = new GlobalControlCommandRegistry();
        using var _ = registry.Register(new EchoCommand());

        var router = new ControlCommandRouter(registry);
        var call = new CommandCallMessage
        {
            Id = "req-1",
            Name = "test.echo",
            Args = JObject.Parse("{\"Message\":\"hi\"}"),
        };

        var result = (CommandResultMessage)router.Execute(call);

        Assert.Equal("ok", result.Status);
        Assert.Equal("hi", (string?)result.Output["Echoed"]);
        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public void Execute_WithInvalidArgs_ReturnsFailedStatusAndDiagnostic()
    {
        var registry = new GlobalControlCommandRegistry();
        using var _ = registry.Register(new EchoCommand());
        var router = new ControlCommandRouter(registry);

        var call = new CommandCallMessage
        {
            Id = "req-2",
            Name = "test.echo",
            Args = JObject.Parse("{\"Message\": 42}"), // wrong type
        };

        var result = (CommandResultMessage)router.Execute(call);

        Assert.Equal("failed", result.Status);
        // diagnostic content is implementation-specific; just assert
        // we got a failed wire shape and some diagnostic.
        Assert.NotNull(result.Error);
    }
}
```

- [ ] **Step 2: Run, expect PASS**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~TypedCommandRoundTripTests
```

Expected: all PASS (the foundation works end-to-end).

- [ ] **Step 3: Commit**

```bash
git add tests/HotRepl.Tests/Integration/TypedCommandRoundTripTests.cs
git commit -m "test(core): typed-command end-to-end round trip through router"
```

---

### A17: Full Core test sweep + ILRepack verification

- [ ] **Step 1: Run the full test suite**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q
```

Expected: every test passes. If any test fails, fix it in place (most failures will be test-side
references to old shapes that A15-Step 6 missed). Re-run until clean.

- [ ] **Step 2: Build Release and inspect output**

```bash
dotnet build src/HotRepl.Core/ -c Release --nologo -v q
ls src/HotRepl.Core/bin/Release/netstandard2.1/
```

Expected output directory contains:

- `HotRepl.Core.dll` (~3 MB)
- `Newtonsoft.Json.dll`
- (any other consumer-facing reference assemblies — Fleck, etc.)

NOT in output:

- `NJsonSchema.dll`
- `Namotion.Reflection.dll`

If `NJsonSchema.dll` or `Namotion.Reflection.dll` appear, the ILRepack target failed to delete them
— fix the `<Delete>` step in `ILRepack.targets`.

- [ ] **Step 3: Lefthook pre-push gate**

```bash
lefthook run pre-push --force
```

Expected: green across the board.

- [ ] **Step 4: Commit (if any cleanup landed)**

```bash
git status
git diff
# If anything is uncommitted from this sweep:
git add -A
git commit -m "chore(core): test/lint cleanup after typed-command refactor"
```

---

## Workstream B — `HotRepl.UnityCommands` plugin

B depends on A being complete. Within B, tasks B1-B5 (shared source + POCOs + commands) can be done
in any order; B6-B7 (loader plugins) depend on B1-B5; B8 (host wiring) depends on B6-B7.

### B1: Shared source folder skeleton + `Vec3` POCO

**Files:**

- Create: `src/HotRepl.UnityCommands/` (folder)
- Create: `src/HotRepl.UnityCommands/Vec3.cs`

- [ ] **Step 1: Create the folder + Vec3**

`src/HotRepl.UnityCommands/Vec3.cs`:

```csharp
using System.ComponentModel;

namespace HotRepl.UnityCommands;

/// <summary>
/// JSON-friendly vector POCO used in UnityCommands schemas instead of
/// <see cref="UnityEngine.Vector3"/>, which carries Unity-internal
/// surface that NJsonSchema reflects over and emits noise for.
/// </summary>
public sealed class Vec3
{
    [Description("X component.")]
    public float X { get; set; }

    [Description("Y component.")]
    public float Y { get; set; }

    [Description("Z component.")]
    public float Z { get; set; }

    public static Vec3 From(UnityEngine.Vector3 v) => new() { X = v.x, Y = v.y, Z = v.z };
}
```

- [ ] **Step 2: Commit (folder + Vec3)**

```bash
git add src/HotRepl.UnityCommands/
git commit -m "feat(unity-commands): shared source folder with Vec3 POCO"
```

---

### B2: `unity.app.info` command + POCO

**Files:**

- Create: `src/HotRepl.UnityCommands/Models/UnityAppInfo.cs`
- Create: `src/HotRepl.UnityCommands/Commands/UnityAppInfoCommand.cs`

- [ ] **Step 1: POCO**

```csharp
// src/HotRepl.UnityCommands/Models/UnityAppInfo.cs
using System.ComponentModel;

namespace HotRepl.UnityCommands.Models;

public sealed class UnityAppInfo
{
    [Description("Application.productName as configured in the Unity project.")]
    public string ProductName { get; set; } = "";

    [Description("Unity engine version the game was built with.")]
    public string UnityVersion { get; set; } = "";

    [Description("RuntimePlatform enum value as string.")]
    public string Platform { get; set; } = "";

    [Description("True when running in the Unity Editor (always false at runtime).")]
    public bool IsEditor { get; set; }
}
```

- [ ] **Step 2: Handler**

```csharp
// src/HotRepl.UnityCommands/Commands/UnityAppInfoCommand.cs
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.UnityCommands.Models;

namespace HotRepl.UnityCommands.Commands;

public sealed class UnityAppInfoCommand
    : IControlCommandHandler<EmptyArgs, UnityAppInfo>
{
    public string Name => "unity.app.info";
    public int Version => 1;
    public ControlCommandKind Kind => ControlCommandKind.Synchronous;
    public bool MutatesState => false;

    public ValueTask<ControlCommandResult<UnityAppInfo>> ExecuteAsync(
        ControlCommandContext _, EmptyArgs __, CancellationToken ___)
        => new(ControlCommandResult<UnityAppInfo>.Ok(new UnityAppInfo
        {
            ProductName  = UnityEngine.Application.productName,
            UnityVersion = UnityEngine.Application.unityVersion,
            Platform     = UnityEngine.Application.platform.ToString(),
            IsEditor     = UnityEngine.Application.isEditor,
        }));
}
```

- [ ] **Step 3: Commit**

```bash
git add src/HotRepl.UnityCommands/Models/UnityAppInfo.cs src/HotRepl.UnityCommands/Commands/UnityAppInfoCommand.cs
git commit -m "feat(unity-commands): unity.app.info command"
```

---

### B3: `unity.gameobject.find` command + POCOs

**Files:**

- Create: `src/HotRepl.UnityCommands/Models/UnityGameObjectFindArgs.cs`
- Create: `src/HotRepl.UnityCommands/Models/UnityGameObjectFindResult.cs`
- Create: `src/HotRepl.UnityCommands/Models/UnityGameObject.cs`
- Create: `src/HotRepl.UnityCommands/Commands/UnityGameObjectFindCommand.cs`

- [ ] **Step 1: Args + Result POCOs**

```csharp
// src/HotRepl.UnityCommands/Models/UnityGameObjectFindArgs.cs
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HotRepl.UnityCommands.Models;

public sealed class UnityGameObjectFindArgs
{
    [Required]
    [Description(
        "Hierarchy path. Two forms: a plain name (e.g. 'Player') uses " +
        "GameObject.Find and may match any active root-or-tagged GO; " +
        "a slash-separated path starting with '/' (e.g. " +
        "'/Player/Inventory/Slots') traverses from a scene root."
    )]
    public string Path { get; set; } = "";
}
```

```csharp
// src/HotRepl.UnityCommands/Models/UnityGameObjectFindResult.cs
using System.ComponentModel;

namespace HotRepl.UnityCommands.Models;

public sealed class UnityGameObjectFindResult
{
    [Description("Null when no GameObject matched the path.")]
    public UnityGameObject? GameObject { get; set; }
}
```

```csharp
// src/HotRepl.UnityCommands/Models/UnityGameObject.cs
using System;
using System.ComponentModel;

namespace HotRepl.UnityCommands.Models;

public sealed class UnityGameObject
{
    [Description("Name of the matched GameObject.")]
    public string Name { get; set; } = "";

    [Description("activeInHierarchy state.")]
    public bool ActiveInHierarchy { get; set; }

    [Description("Layer index.")]
    public int Layer { get; set; }

    [Description("Tag string.")]
    public string Tag { get; set; } = "";

    [Description("World-space position.")]
    public Vec3 Position { get; set; } = new();

    [Description("Type names of attached components, in component-order.")]
    public string[] ComponentTypeNames { get; set; } = Array.Empty<string>();
}
```

- [ ] **Step 2: Handler**

```csharp
// src/HotRepl.UnityCommands/Commands/UnityGameObjectFindCommand.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.UnityCommands.Models;

namespace HotRepl.UnityCommands.Commands;

public sealed class UnityGameObjectFindCommand
    : IControlCommandHandler<UnityGameObjectFindArgs, UnityGameObjectFindResult>
{
    public string Name => "unity.gameobject.find";
    public int Version => 1;
    public ControlCommandKind Kind => ControlCommandKind.Synchronous;
    public bool MutatesState => false;

    public ValueTask<ControlCommandResult<UnityGameObjectFindResult>> ExecuteAsync(
        ControlCommandContext _, UnityGameObjectFindArgs args, CancellationToken __)
    {
        var go = UnityEngine.GameObject.Find(args.Path);
        return new(ControlCommandResult<UnityGameObjectFindResult>.Ok(
            new UnityGameObjectFindResult
            {
                GameObject = go is null ? null : ToDto(go),
            }));
    }

    private static UnityGameObject ToDto(UnityEngine.GameObject go)
    {
        var components = go.GetComponents<UnityEngine.Component>();
        var names = new string[components.Length];
        for (int i = 0; i < components.Length; i++)
        {
            names[i] = components[i]?.GetType().FullName ?? "<null>";
        }
        return new UnityGameObject
        {
            Name              = go.name,
            ActiveInHierarchy = go.activeInHierarchy,
            Layer             = go.layer,
            Tag               = go.tag,
            Position          = Vec3.From(go.transform.position),
            ComponentTypeNames = names,
        };
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/HotRepl.UnityCommands/Models/UnityGameObjectFindArgs.cs src/HotRepl.UnityCommands/Models/UnityGameObjectFindResult.cs src/HotRepl.UnityCommands/Models/UnityGameObject.cs src/HotRepl.UnityCommands/Commands/UnityGameObjectFindCommand.cs
git commit -m "feat(unity-commands): unity.gameobject.find command"
```

---

### B4: `unity.time.set_scale` command + POCOs

**Files:**

- Create: `src/HotRepl.UnityCommands/Models/UnitySetTimeScaleArgs.cs`
- Create: `src/HotRepl.UnityCommands/Models/UnitySetTimeScaleResult.cs`
- Create: `src/HotRepl.UnityCommands/Commands/UnityTimeSetScaleCommand.cs`

- [ ] **Step 1: POCOs**

```csharp
// src/HotRepl.UnityCommands/Models/UnitySetTimeScaleArgs.cs
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HotRepl.UnityCommands.Models;

public sealed class UnitySetTimeScaleArgs
{
    [Required, Range(0f, 100f)]
    [Description(
        "New Time.timeScale value. 0 = paused, 1 = normal, 2 = double-speed. " +
        "Values > 1 may exceed safe physics-step bounds in some games."
    )]
    public float TimeScale { get; set; }
}
```

```csharp
// src/HotRepl.UnityCommands/Models/UnitySetTimeScaleResult.cs
using System.ComponentModel;

namespace HotRepl.UnityCommands.Models;

public sealed class UnitySetTimeScaleResult
{
    [Description("Previous Time.timeScale before this call.")]
    public float PreviousTimeScale { get; set; }

    [Description("Time.timeScale after this call.")]
    public float NewTimeScale { get; set; }
}
```

- [ ] **Step 2: Handler**

```csharp
// src/HotRepl.UnityCommands/Commands/UnityTimeSetScaleCommand.cs
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.UnityCommands.Models;

namespace HotRepl.UnityCommands.Commands;

public sealed class UnityTimeSetScaleCommand
    : IControlCommandHandler<UnitySetTimeScaleArgs, UnitySetTimeScaleResult>
{
    public string Name => "unity.time.set_scale";
    public int Version => 1;
    public ControlCommandKind Kind => ControlCommandKind.Synchronous;
    public bool MutatesState => true;

    public ValueTask<ControlCommandResult<UnitySetTimeScaleResult>> ExecuteAsync(
        ControlCommandContext _, UnitySetTimeScaleArgs args, CancellationToken __)
    {
        var previous = UnityEngine.Time.timeScale;
        UnityEngine.Time.timeScale = args.TimeScale;
        return new(ControlCommandResult<UnitySetTimeScaleResult>.Ok(
            new UnitySetTimeScaleResult
            {
                PreviousTimeScale = previous,
                NewTimeScale      = UnityEngine.Time.timeScale,
            }));
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/HotRepl.UnityCommands/Models/UnitySetTimeScaleArgs.cs src/HotRepl.UnityCommands/Models/UnitySetTimeScaleResult.cs src/HotRepl.UnityCommands/Commands/UnityTimeSetScaleCommand.cs
git commit -m "feat(unity-commands): unity.time.set_scale command"
```

---

### B5: `unity.screenshot.capture` command + POCOs

**Files:**

- Create: `src/HotRepl.UnityCommands/Models/UnityScreenshotArgs.cs`
- Create: `src/HotRepl.UnityCommands/Models/UnityScreenshotResult.cs`
- Create: `src/HotRepl.UnityCommands/Commands/UnityScreenshotCommand.cs`

- [ ] **Step 1: POCOs**

```csharp
// src/HotRepl.UnityCommands/Models/UnityScreenshotArgs.cs
using System.ComponentModel;

namespace HotRepl.UnityCommands.Models;

public sealed class UnityScreenshotArgs
{
    [Description("Super-sampling factor. Default 1.")]
    public int SuperSize { get; set; } = 1;
}
```

```csharp
// src/HotRepl.UnityCommands/Models/UnityScreenshotResult.cs
using System.ComponentModel;

namespace HotRepl.UnityCommands.Models;

public sealed class UnityScreenshotResult
{
    [Description("Width of the captured frame in pixels.")]
    public int Width { get; set; }

    [Description("Height of the captured frame in pixels.")]
    public int Height { get; set; }
}
```

(The `ArtifactRef` for the captured PNG goes in the top-level `Artifacts` map of the wire result,
NOT in this POCO.)

- [ ] **Step 2: Handler**

```csharp
// src/HotRepl.UnityCommands/Commands/UnityScreenshotCommand.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.UnityCommands.Models;

namespace HotRepl.UnityCommands.Commands;

public sealed class UnityScreenshotCommand
    : IControlCommandHandler<UnityScreenshotArgs, UnityScreenshotResult>
{
    public string Name => "unity.screenshot.capture";
    public int Version => 1;
    public ControlCommandKind Kind => ControlCommandKind.Synchronous;
    public bool MutatesState => false;

    public async ValueTask<ControlCommandResult<UnityScreenshotResult>> ExecuteAsync(
        ControlCommandContext context,
        UnityScreenshotArgs args,
        CancellationToken ct)
    {
        var superSize = Math.Max(1, args.SuperSize);
        var tex = UnityEngine.ScreenCapture.CaptureScreenshotAsTexture(superSize);
        try
        {
            byte[] png = UnityEngine.ImageConversion.EncodeToPNG(tex);
            int width = tex.width, height = tex.height;

            var artifact = await context.Artifacts.WriteAsync(
                logicalName: "screenshot",
                bytes: png,
                contentType: "image/png",
                cancellationToken: ct
            ).ConfigureAwait(true);

            return ControlCommandResult<UnityScreenshotResult>.Ok(
                new UnityScreenshotResult { Width = width, Height = height },
                "screenshot",
                artifact
            );
        }
        finally
        {
            UnityEngine.Object.Destroy(tex);
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/HotRepl.UnityCommands/Models/UnityScreenshotArgs.cs src/HotRepl.UnityCommands/Models/UnityScreenshotResult.cs src/HotRepl.UnityCommands/Commands/UnityScreenshotCommand.cs
git commit -m "feat(unity-commands): unity.screenshot.capture command"
```

---

### B6: `UnityCommandCatalog`

**Files:** Create `src/HotRepl.UnityCommands/UnityCommandCatalog.cs`

- [ ] **Step 1: Write the catalog**

```csharp
// src/HotRepl.UnityCommands/UnityCommandCatalog.cs
using System;
using System.Collections.Generic;
using HotRepl.Control;
using HotRepl.UnityCommands.Commands;
using HotRepl.UnityCommands.Models;

namespace HotRepl.UnityCommands;

/// <summary>
/// Catalog of UnityCommands handlers. Used by both loader plugins
/// (BepInEx and MelonLoader) to register the same set of commands
/// against the host's <see cref="GlobalControlCommandRegistry"/>.
/// </summary>
public static class UnityCommandCatalog
{
    /// <summary>
    /// Returns one delegate per command that, when called with the
    /// registry, registers the command and returns the disposable
    /// registration.
    /// </summary>
    public static IReadOnlyList<RegistrationFactory> Build() => new RegistrationFactory[]
    {
        registry => registry.Register<EmptyArgs, UnityAppInfo>(new UnityAppInfoCommand()),
        registry => registry.Register<UnityGameObjectFindArgs, UnityGameObjectFindResult>(new UnityGameObjectFindCommand()),
        registry => registry.Register<UnitySetTimeScaleArgs, UnitySetTimeScaleResult>(new UnityTimeSetScaleCommand()),
        registry => registry.Register<UnityScreenshotArgs, UnityScreenshotResult>(new UnityScreenshotCommand()),
    };

    /// <summary>Names of every command in the catalog, in registration order.</summary>
    public static IReadOnlyList<string> Names { get; } = new[]
    {
        "unity.app.info",
        "unity.gameobject.find",
        "unity.time.set_scale",
        "unity.screenshot.capture",
    };

    public delegate IDisposable RegistrationFactory(IControlCommandRegistry registry);
}
```

- [ ] **Step 2: Catalog test**

`tests/HotRepl.Tests/Unit/UnityCommandsCatalogTests.cs`:

```csharp
using HotRepl.Control;
using HotRepl.UnityCommands;
using Xunit;

namespace HotRepl.Tests.Unit;

public class UnityCommandsCatalogTests
{
    [Fact]
    public void Build_ProducesExactlyFourRegistrations()
    {
        var registry = new GlobalControlCommandRegistry();
        var factories = UnityCommandCatalog.Build();
        Assert.Equal(4, factories.Count);
        foreach (var f in factories) f(registry);

        var descriptors = registry.Describe();
        Assert.Equal(4, descriptors.Count);
        Assert.Contains(descriptors, d => d.Name == "unity.app.info");
        Assert.Contains(descriptors, d => d.Name == "unity.gameobject.find");
        Assert.Contains(descriptors, d => d.Name == "unity.time.set_scale");
        Assert.Contains(descriptors, d => d.Name == "unity.screenshot.capture");
    }

    [Fact]
    public void TimeSetScale_AdvertisesMutatesState()
    {
        var registry = new GlobalControlCommandRegistry();
        foreach (var f in UnityCommandCatalog.Build()) f(registry);

        var time = System.Linq.Enumerable.First(
            registry.Describe(),
            d => d.Name == "unity.time.set_scale");
        Assert.True(time.MutatesState);
    }
}
```

The test project can reference `HotRepl.UnityCommands` only if it compiles standalone — but the
source folder has UnityEngine dependencies. The catalog test can't actually link against UnityEngine
in the test project. So either:

(a) Move the catalog test into one of the loader-csproj test projects (which DO reference
UnityEngine via their parent csproj), or

(b) Constrain the catalog test to only check `UnityCommandCatalog.Names` (a static string list).

**Pick (b)** for v1 — it tests the contract without forcing the test project to grow a UnityEngine
reference. The behavior of each command is tested by live verification (Workstream B step B9).

Revised test:

```csharp
using HotRepl.UnityCommands;
using Xunit;

namespace HotRepl.Tests.Unit;

public class UnityCommandsCatalogTests
{
    [Fact]
    public void Names_AreTheExpectedFour()
    {
        Assert.Equal(new[]
        {
            "unity.app.info",
            "unity.gameobject.find",
            "unity.time.set_scale",
            "unity.screenshot.capture",
        }, UnityCommandCatalog.Names);
    }
}
```

But this requires `HotRepl.Tests` to reference the shared source. The simplest setup: have
`HotRepl.Tests.csproj` `<Compile Include>` **only** `UnityCommandCatalog.cs` (not the Commands/
folder which needs UnityEngine). The `Names` array doesn't depend on the command types.

Add to `tests/HotRepl.Tests/HotRepl.Tests.csproj`:

```xml
<ItemGroup>
  <!-- Test only the names array, which doesn't require UnityEngine. -->
  <Compile Include="../../src/HotRepl.UnityCommands/UnityCommandCatalog.cs"
           Link="ImportedCatalog/UnityCommandCatalog.cs" />
</ItemGroup>
```

But this link breaks because the source file references the command types. **Simplest workable
fix**: extract `UnityCommandCatalog.Names` into its own file:

```csharp
// src/HotRepl.UnityCommands/UnityCommandCatalogNames.cs
namespace HotRepl.UnityCommands;

public static class UnityCommandCatalogNames
{
    public static IReadOnlyList<string> Names { get; } = new[]
    {
        "unity.app.info",
        "unity.gameobject.find",
        "unity.time.set_scale",
        "unity.screenshot.capture",
    };
}
```

The test references `UnityCommandCatalogNames.Names`. The full catalog
(`UnityCommandCatalog.Build()`) still lives in the main file with the actual handler types and is
exercised by the live verification.

- [ ] **Step 3: Run the test**

```bash
dotnet test tests/HotRepl.Tests/ --nologo -v q --filter FullyQualifiedName~UnityCommandsCatalogTests
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/HotRepl.UnityCommands/UnityCommandCatalog.cs src/HotRepl.UnityCommands/UnityCommandCatalogNames.cs tests/HotRepl.Tests/Unit/UnityCommandsCatalogTests.cs tests/HotRepl.Tests/HotRepl.Tests.csproj
git commit -m "feat(unity-commands): catalog + names test"
```

---

### B7: BepInEx loader project + plugin

**Files:**

- Create: `src/HotRepl.UnityCommands.BepInEx/HotRepl.UnityCommands.BepInEx.csproj`
- Create: `src/HotRepl.UnityCommands.BepInEx/Plugin.cs`

- [ ] **Step 1: csproj**

Look at the existing `src/HotRepl.BepInEx/HotRepl.BepInEx.csproj` for the Unity reference +
ProjectReference patterns. Copy and adapt:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AnalysisMode>Recommended</AnalysisMode>
    <RootNamespace>HotRepl.UnityCommands</RootNamespace>
    <AssemblyName>HotRepl.UnityCommands.BepInEx</AssemblyName>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BepInEx.Core" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../HotRepl.Core/HotRepl.Core.csproj" />
  </ItemGroup>

  <!-- Mono UnityEngine reference, mirrors HotRepl.BepInEx -->
  <ItemGroup>
    <Reference Include="UnityEngine">
      <HintPath>../HotRepl.BepInEx/lib/UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>../HotRepl.BepInEx/lib/UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.IMGUIModule">
      <HintPath>../HotRepl.BepInEx/lib/UnityEngine.IMGUIModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <!-- Shared source folder compiled against this csproj's Unity references. -->
  <ItemGroup>
    <Compile Include="..\HotRepl.UnityCommands\**\*.cs"
             Exclude="..\HotRepl.UnityCommands\bin\**;..\HotRepl.UnityCommands\obj\**" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Plugin.cs**

```csharp
// src/HotRepl.UnityCommands.BepInEx/Plugin.cs
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HotRepl.Control;
using HotRepl.UnityCommands;

namespace HotRepl.UnityCommands.BepInEx;

[BepInPlugin(PluginGuid, "HotRepl Unity Commands", "3.0.0")]
[BepInDependency("hotrepl.bepinex", BepInDependency.DependencyFlags.HardDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "hotrepl.unitycommands.bepinex";

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
            "Comma-separated command names to skip (e.g. 'unity.time.set_scale, unity.screenshot.capture'). " +
            "Useful when a consumer's own mod registers a command with the same name."
        );

        if (!enabled.Value)
        {
            Logger.LogInfo("HotRepl.UnityCommands disabled via config; skipping registration.");
            return;
        }

        var skip = ParseCsv(disabled.Value);
        var registry = GlobalControlCommandRegistry.Instance;
        var factories = UnityCommandCatalog.Build();
        var names = UnityCommandCatalogNames.Names;

        for (int i = 0; i < factories.Count; i++)
        {
            if (skip.Contains(names[i]))
            {
                Logger.LogInfo($"Skipping disabled command: {names[i]}");
                continue;
            }
            _registrations.Add(factories[i](registry));
            Logger.LogInfo($"Registered: {names[i]}");
        }
    }

    private void OnDestroy()
    {
        foreach (var r in _registrations) r.Dispose();
        _registrations.Clear();
    }

    private static HashSet<string> ParseCsv(string csv)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(csv)) return set;
        foreach (var raw in csv.Split(','))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length > 0) set.Add(trimmed);
        }
        return set;
    }
}
```

- [ ] **Step 3: Build the BepInEx loader csproj**

```bash
dotnet build src/HotRepl.UnityCommands.BepInEx/ --nologo -v q
```

Expected: clean build. Failure usually means `lib/UnityEngine.dll` etc. aren't where the csproj
expects — adjust the `<HintPath>` to wherever the existing `HotRepl.BepInEx` host stores them in
this checkout.

- [ ] **Step 4: Commit**

```bash
git add src/HotRepl.UnityCommands.BepInEx/
git commit -m "feat(unity-commands): BepInEx loader plugin"
```

---

### B8: MelonLoader loader project + mod

**Files:**

- Create: `src/HotRepl.UnityCommands.MelonLoader/HotRepl.UnityCommands.MelonLoader.csproj`
- Create: `src/HotRepl.UnityCommands.MelonLoader/Mod.cs`

- [ ] **Step 1: csproj**

Look at `src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj` for the IL2CPP-unhollowed
UnityEngine reference shape. Copy + adapt:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>HotRepl.UnityCommands.MelonLoader</RootNamespace>
    <AssemblyName>HotRepl.UnityCommands.MelonLoader</AssemblyName>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <PropertyGroup>
    <MelonLoaderPath Condition="'$(MelonLoaderPath)' == ''">$(MelonLoaderPath_FromExistingHost)</MelonLoaderPath>
    <Il2CppAssembliesPath Condition="'$(Il2CppAssembliesPath)' == ''">$(Il2CppAssembliesPath_FromExistingHost)</Il2CppAssembliesPath>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="MelonLoader">
      <HintPath>$(MelonLoaderPath)/MelonLoader.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>$(Il2CppAssembliesPath)/UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(Il2CppAssembliesPath)/UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.IMGUIModule">
      <HintPath>$(Il2CppAssembliesPath)/UnityEngine.IMGUIModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../HotRepl.Core/HotRepl.Core.csproj" />
    <ProjectReference Include="../HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Compile Include="..\HotRepl.UnityCommands\**\*.cs"
             Exclude="..\HotRepl.UnityCommands\bin\**;..\HotRepl.UnityCommands\obj\**" />
  </ItemGroup>
</Project>
```

Mirror whatever the existing `HotRepl.Host.MelonLoader` csproj does for the `MelonLoaderPath` /
`Il2CppAssembliesPath` resolution (commonly passed via `dotnet build -p:MelonLoaderPath=...`).

- [ ] **Step 2: Mod.cs**

```csharp
// src/HotRepl.UnityCommands.MelonLoader/Mod.cs
using System;
using System.Collections.Generic;
using HotRepl.Control;
using HotRepl.UnityCommands;
using MelonLoader;

[assembly: MelonInfo(typeof(HotRepl.UnityCommands.MelonLoader.Mod),
    "HotRepl Unity Commands", "3.0.0", "glockyco")]

namespace HotRepl.UnityCommands.MelonLoader;

public sealed class Mod : MelonMod
{
    private readonly List<IDisposable> _registrations = new();
    private MelonPreferences_Entry<bool> _enabled = null!;
    private MelonPreferences_Entry<string> _disabled = null!;

    public override void OnInitializeMelon()
    {
        var category = MelonPreferences.CreateCategory("HotRepl.UnityCommands");
        _enabled = category.CreateEntry(
            "Enabled", true,
            description: "Master switch. When false, no UnityCommands handlers are registered. Changes apply on next game start."
        );
        _disabled = category.CreateEntry(
            "Disabled", "",
            description: "Comma-separated command names to skip."
        );
    }

    public override void OnLateInitializeMelon()
    {
        // Runs after every OnInitializeMelon across all mods — the
        // HotRepl host has registered its registry singleton by now.
        if (!_enabled.Value)
        {
            LoggerInstance.Msg("HotRepl.UnityCommands disabled via config; skipping registration.");
            return;
        }

        var skip = ParseCsv(_disabled.Value);
        var registry = GlobalControlCommandRegistry.Instance;
        if (registry == null)
        {
            LoggerInstance.Warning("GlobalControlCommandRegistry.Instance is null; HotRepl host not loaded?");
            return;
        }

        var factories = UnityCommandCatalog.Build();
        var names = UnityCommandCatalogNames.Names;
        for (int i = 0; i < factories.Count; i++)
        {
            if (skip.Contains(names[i]))
            {
                LoggerInstance.Msg($"Skipping disabled command: {names[i]}");
                continue;
            }
            _registrations.Add(factories[i](registry));
            LoggerInstance.Msg($"Registered: {names[i]}");
        }
    }

    public override void OnDeinitializeMelon()
    {
        foreach (var r in _registrations) r.Dispose();
        _registrations.Clear();
    }

    private static HashSet<string> ParseCsv(string csv)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(csv)) return set;
        foreach (var raw in csv.Split(','))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length > 0) set.Add(trimmed);
        }
        return set;
    }
}
```

- [ ] **Step 3: Build (requires MelonLoader paths)**

```bash
dotnet build src/HotRepl.UnityCommands.MelonLoader/HotRepl.UnityCommands.MelonLoader.csproj \
  -p:MelonLoaderPath="<path-to-MelonLoader>" \
  -p:Il2CppAssembliesPath="<path-to-MelonLoader>/Il2CppAssemblies" \
  --nologo -v q
```

If paths aren't available on this machine, this build can be skipped here — it'll be exercised when
a consumer (Ancient Kingdoms) tries the deployment in Phase 3. Document that and move on.

- [ ] **Step 4: Commit**

```bash
git add src/HotRepl.UnityCommands.MelonLoader/
git commit -m "feat(unity-commands): MelonLoader loader mod"
```

---

### B9: Wire UnityCommands into host csprojs (project reference for bundled distribution)

**Files:**

- Modify: `src/HotRepl.BepInEx/HotRepl.BepInEx.csproj`
- Modify: `src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj`

- [ ] **Step 1: Add project reference in the BepInEx host**

Add inside the `<ItemGroup>` that already contains project references:

```xml
<ProjectReference Include="../HotRepl.UnityCommands.BepInEx/HotRepl.UnityCommands.BepInEx.csproj" />
```

- [ ] **Step 2: Add project reference in the MelonLoader host**

Same pattern:

```xml
<ProjectReference Include="../HotRepl.UnityCommands.MelonLoader/HotRepl.UnityCommands.MelonLoader.csproj" />
```

- [ ] **Step 3: Build BepInEx host and inspect output**

```bash
dotnet build src/HotRepl.BepInEx/ --nologo -v q
ls src/HotRepl.BepInEx/bin/Debug/netstandard2.1/ | grep -i "hotrepl\|unitycommands"
```

Expected: `HotRepl.BepInEx.dll`, `HotRepl.Core.dll`, `HotRepl.UnityCommands.BepInEx.dll`, etc.

- [ ] **Step 4: Commit**

```bash
git add src/HotRepl.BepInEx/HotRepl.BepInEx.csproj src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj
git commit -m "feat(hosts): bundle HotRepl.UnityCommands with both host plugins"
```

---

### B10: Live BepInEx verification (manual)

This validates the bundled deployment end-to-end against a real game. The MelonLoader path follows
the same pattern but is exercised in Phase 3 (AK migration) since AK is HotRepl's primary IL2CPP
consumer.

- [ ] **Step 1: Deploy to a BepInEx-instrumented game** (e.g., Ardenfall Demo via the existing
      `ardenfall-compendium` setup)

```bash
cd ~/Projects/ardenfall-compendium
bun run hotrepl:setup
```

- [ ] **Step 2: Launch the game** (via `bun run hotrepl:launch` or manual Steam launch)

- [ ] **Step 3: Verify the 4 commands show up**

From the HotRepl repo root with `/tmp/hotrepl-smoke` set up (per `scripts/verify-live-ardenfall.ts`
instructions), or directly:

```bash
bunx @hotrepl/cli list-commands | grep '^unity\.'
```

Expected output:

```
unity.app.info
unity.gameobject.find
unity.screenshot.capture
unity.time.set_scale
```

- [ ] **Step 4: Exercise each command**

```bash
bunx @hotrepl/cli run unity.app.info '{}'
# → { "productName": "Ardenfall", "unityVersion": "...", "platform": "...", "isEditor": false }

bunx @hotrepl/cli run unity.gameobject.find '{"path": "/"}'
# → Returns root GameObject info

bunx @hotrepl/cli run unity.time.set_scale '{"timeScale": 0.5}'
# → { "previousTimeScale": 1, "newTimeScale": 0.5 }
# Game visibly slows. Reset:
bunx @hotrepl/cli run unity.time.set_scale '{"timeScale": 1}'

bunx @hotrepl/cli run unity.screenshot.capture '{}'
# → { "width": ..., "height": ..., "artifacts": { "screenshot": { ... } } }
```

- [ ] **Step 5: Negative test — validation**

```bash
bunx @hotrepl/cli run unity.time.set_scale '{"timeScale": -1}'
```

Expected: `status: "failed"`, diagnostic with kind `validation_failed`. Game's `Time.timeScale` is
NOT changed.

- [ ] **Step 6: Document results** in a one-paragraph note for the eventual verification spec, OR
      commit a small snapshot of the working output to the spec doc.

(No code commit at this step; manual verification only.)

---

## Workstream C — `glockyco/hotrepl-mod-template` repo

This work happens in a NEW repository, not in the HotRepl repo. Steps below assume the working
directory is the new template repo unless stated otherwise.

### C1: Create the GitHub repository

- [ ] **Step 1: Create the empty repo on GitHub**

Via the GitHub web UI: create `glockyco/hotrepl-mod-template`. Mark it as a "template repository" so
the "Use this template" button is exposed. License: MIT. Description: "Scaffold for a HotRepl mod
(BepInEx + MelonLoader)."

- [ ] **Step 2: Clone locally**

```bash
cd ~/Projects
git clone git@github.com:glockyco/hotrepl-mod-template.git
cd hotrepl-mod-template
```

---

### C2: Repo skeleton (root files)

- [ ] **Step 1: `.gitignore`**

```
bin/
obj/
*.user
.vs/
Local.props
.idea/
.DS_Store
```

- [ ] **Step 2: `.editorconfig`**

```ini
root = true

[*]
charset = utf-8
indent_style = space
indent_size = 4
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true

[*.{json,yml,yaml,md}]
indent_size = 2
```

- [ ] **Step 3: `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisMode>Recommended</AnalysisMode>
  </PropertyGroup>

  <!-- Import Local.props if present for game-path overrides. -->
  <Import Project="$(MSBuildThisFileDirectory)Local.props"
          Condition="Exists('$(MSBuildThisFileDirectory)Local.props')" />
</Project>
```

- [ ] **Step 4: `Local.props.example`**

```xml
<!--
  Copy this file to Local.props and fill in your game paths.
  Local.props is gitignored.
-->
<Project>
  <PropertyGroup>
    <!-- BepInEx game install (used by scripts/deploy-bepinex.sh) -->
    <BepInExGamePath>/path/to/Game/with/BepInEx/plugins</BepInExGamePath>

    <!-- MelonLoader game install (used by scripts/deploy-melonloader.sh) -->
    <MelonLoaderPath>/path/to/Game/MelonLoader</MelonLoaderPath>
    <Il2CppAssembliesPath>/path/to/Game/MelonLoader/Il2CppAssemblies</Il2CppAssembliesPath>
    <MelonLoaderGamePath>/path/to/Game/with/MelonLoader/Mods</MelonLoaderGamePath>
  </PropertyGroup>
</Project>
```

- [ ] **Step 5: `LICENSE`** — copy MIT text, year 2026, holder "glockyco"

- [ ] **Step 6: Initial commit**

```bash
git add .gitignore .editorconfig Directory.Build.props Local.props.example LICENSE
git commit -m "chore: repo skeleton (gitignore, editorconfig, build props, license)"
```

---

### C3: Shared source folder + demo command

**Files:**

- Create: `src/MyMod/Models/HelloWorldArgs.cs`
- Create: `src/MyMod/Models/HelloWorldResult.cs`
- Create: `src/MyMod/Commands/HelloWorldCommand.cs`
- Create: `src/MyMod/MyModCatalog.cs`

- [ ] **Step 1: POCOs**

```csharp
// src/MyMod/Models/HelloWorldArgs.cs
using System.ComponentModel;

namespace MyMod.Models;

public sealed class HelloWorldArgs
{
    [Description("Optional name. Defaults to 'world'.")]
    public string? Name { get; set; }
}
```

```csharp
// src/MyMod/Models/HelloWorldResult.cs
using System;
using System.ComponentModel;

namespace MyMod.Models;

public sealed class HelloWorldResult
{
    [Description("The greeting that was produced.")]
    public string Greeting { get; set; } = "";

    [Description("Server-side timestamp when the greeting was generated, ISO 8601 UTC.")]
    public DateTimeOffset GeneratedAt { get; set; }
}
```

- [ ] **Step 2: Handler**

```csharp
// src/MyMod/Commands/HelloWorldCommand.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using MyMod.Models;

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
///   $ hotrepl run mymod.hello '{"name":"Ardenfall"}'
/// </summary>
public sealed class HelloWorldCommand
    : IControlCommandHandler<HelloWorldArgs, HelloWorldResult>
{
    public string Name => "mymod.hello";
    public int Version => 1;
    public ControlCommandKind Kind => ControlCommandKind.Synchronous;
    public bool MutatesState => false;

    public ValueTask<ControlCommandResult<HelloWorldResult>> ExecuteAsync(
        ControlCommandContext _, HelloWorldArgs args, CancellationToken __)
    {
        var who = string.IsNullOrWhiteSpace(args.Name) ? "world" : args.Name;
        return new(ControlCommandResult<HelloWorldResult>.Ok(new HelloWorldResult
        {
            Greeting = $"Hello, {who}!",
            GeneratedAt = DateTimeOffset.UtcNow,
        }));
    }
}
```

- [ ] **Step 3: Catalog**

```csharp
// src/MyMod/MyModCatalog.cs
using System;
using System.Collections.Generic;
using HotRepl.Control;
using MyMod.Commands;
using MyMod.Models;

namespace MyMod;

public static class MyModCatalog
{
    public static IReadOnlyList<Func<IControlCommandRegistry, IDisposable>> Build()
        => new Func<IControlCommandRegistry, IDisposable>[]
        {
            r => r.Register<HelloWorldArgs, HelloWorldResult>(new HelloWorldCommand()),
        };
}
```

- [ ] **Step 4: Commit**

```bash
git add src/MyMod/
git commit -m "feat: demo HelloWorldCommand using IControlCommandHandler<TArgs, TOutput>"
```

---

### C4: BepInEx loader project

**Files:**

- Create: `src/MyMod.BepInEx/MyMod.BepInEx.csproj`
- Create: `src/MyMod.BepInEx/Plugin.cs`
- Create: `src/MyMod.BepInEx/PluginInfo.cs`

- [ ] **Step 1: csproj**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <RootNamespace>MyMod.BepInEx</RootNamespace>
    <AssemblyName>MyMod.BepInEx</AssemblyName>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BepInEx.Core" Version="5.4.21" PrivateAssets="all" />
    <PackageReference Include="HotRepl.Core" Version="3.0.0" />
  </ItemGroup>

  <!-- Game's UnityEngine.dll. Set BepInExGamePath in Local.props. -->
  <ItemGroup>
    <Reference Include="UnityEngine">
      <HintPath>$(BepInExGamePath)/../../$(GameDataDir)/Managed/UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(BepInExGamePath)/../../$(GameDataDir)/Managed/UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <Compile Include="..\MyMod\**\*.cs"
             Exclude="..\MyMod\bin\**;..\MyMod\obj\**" />
  </ItemGroup>
</Project>
```

(The `GameDataDir` token is game-specific; the README explains.)

- [ ] **Step 2: PluginInfo.cs**

```csharp
// src/MyMod.BepInEx/PluginInfo.cs
namespace MyMod.BepInEx;

internal static class PluginInfo
{
    public const string PluginGuid    = "PLUGIN_GUID_PLACEHOLDER";
    public const string PluginName    = "MyMod";
    public const string PluginVersion = "0.1.0";
}
```

- [ ] **Step 3: Plugin.cs**

```csharp
// src/MyMod.BepInEx/Plugin.cs
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HotRepl.Control;
using MyMod;

namespace MyMod.BepInEx;

[BepInPlugin(PluginInfo.PluginGuid, PluginInfo.PluginName, PluginInfo.PluginVersion)]
[BepInDependency("hotrepl.bepinex", BepInDependency.DependencyFlags.HardDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    private readonly List<IDisposable> _registrations = new();

    private void Awake()
    {
        var enabled = Config.Bind("General", "Enabled", true,
            "Master switch. Changes apply on next game start.");

        if (!enabled.Value)
        {
            Logger.LogInfo($"{PluginInfo.PluginName} disabled via config.");
            return;
        }

        var registry = GlobalControlCommandRegistry.Instance;
        foreach (var factory in MyModCatalog.Build())
        {
            _registrations.Add(factory(registry));
        }
        Logger.LogInfo($"{PluginInfo.PluginName} registered {_registrations.Count} commands.");
    }

    private void OnDestroy()
    {
        foreach (var r in _registrations) r.Dispose();
        _registrations.Clear();
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add src/MyMod.BepInEx/
git commit -m "feat: BepInEx loader plugin scaffold"
```

---

### C5: MelonLoader loader project

**Files:**

- Create: `src/MyMod.MelonLoader/MyMod.MelonLoader.csproj`
- Create: `src/MyMod.MelonLoader/Mod.cs`

- [ ] **Step 1: csproj**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <RootNamespace>MyMod.MelonLoader</RootNamespace>
    <AssemblyName>MyMod.MelonLoader</AssemblyName>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="HotRepl.Core" Version="3.0.0" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="MelonLoader">
      <HintPath>$(MelonLoaderPath)/MelonLoader.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>$(Il2CppAssembliesPath)/UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(Il2CppAssembliesPath)/UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <Compile Include="..\MyMod\**\*.cs"
             Exclude="..\MyMod\bin\**;..\MyMod\obj\**" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Mod.cs**

```csharp
// src/MyMod.MelonLoader/Mod.cs
using System;
using System.Collections.Generic;
using HotRepl.Control;
using MelonLoader;
using MyMod;

[assembly: MelonInfo(typeof(MyMod.MelonLoader.Mod), "MyMod", "0.1.0", "AUTHOR_PLACEHOLDER")]

namespace MyMod.MelonLoader;

public sealed class Mod : MelonMod
{
    private readonly List<IDisposable> _registrations = new();
    private MelonPreferences_Entry<bool> _enabled = null!;

    public override void OnInitializeMelon()
    {
        var category = MelonPreferences.CreateCategory("MyMod");
        _enabled = category.CreateEntry("Enabled", true,
            description: "Master switch. Changes apply on next game start.");
    }

    public override void OnLateInitializeMelon()
    {
        if (!_enabled.Value)
        {
            LoggerInstance.Msg("MyMod disabled via config.");
            return;
        }

        var registry = GlobalControlCommandRegistry.Instance;
        if (registry == null)
        {
            LoggerInstance.Warning("GlobalControlCommandRegistry not available; HotRepl host missing?");
            return;
        }

        foreach (var factory in MyModCatalog.Build())
        {
            _registrations.Add(factory(registry));
        }
        LoggerInstance.Msg($"MyMod registered {_registrations.Count} commands.");
    }

    public override void OnDeinitializeMelon()
    {
        foreach (var r in _registrations) r.Dispose();
        _registrations.Clear();
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/MyMod.MelonLoader/
git commit -m "feat: MelonLoader loader mod scaffold"
```

---

### C6: Deploy scripts

**Files:**

- Create: `scripts/deploy-bepinex.sh`
- Create: `scripts/deploy-melonloader.sh`

- [ ] **Step 1: `scripts/deploy-bepinex.sh`**

```bash
#!/usr/bin/env bash
# Builds the BepInEx loader and copies the DLL into the game's plugin folder.
# Reads BepInExGamePath from Local.props.
set -euo pipefail

GAME_PATH=$(grep -oE 'BepInExGamePath>[^<]+' Local.props | cut -d'>' -f2)
if [[ -z "$GAME_PATH" ]]; then
  echo "Set BepInExGamePath in Local.props (copy from Local.props.example)." >&2
  exit 1
fi

dotnet build src/MyMod.BepInEx -c Debug --nologo -v q

DEST="$GAME_PATH/MyMod"
mkdir -p "$DEST"
cp -f src/MyMod.BepInEx/bin/Debug/netstandard2.1/MyMod.BepInEx.dll "$DEST/"
echo "Deployed MyMod.BepInEx.dll to $DEST"
```

- [ ] **Step 2: `scripts/deploy-melonloader.sh`**

```bash
#!/usr/bin/env bash
# Builds the MelonLoader loader and copies the DLL into the game's Mods folder.
set -euo pipefail

GAME_PATH=$(grep -oE 'MelonLoaderGamePath>[^<]+' Local.props | cut -d'>' -f2)
if [[ -z "$GAME_PATH" ]]; then
  echo "Set MelonLoaderGamePath in Local.props (copy from Local.props.example)." >&2
  exit 1
fi

dotnet build src/MyMod.MelonLoader -c Debug --nologo -v q

cp -f src/MyMod.MelonLoader/bin/Debug/net6.0/MyMod.MelonLoader.dll "$GAME_PATH/"
echo "Deployed MyMod.MelonLoader.dll to $GAME_PATH"
```

- [ ] **Step 3: Make executable + commit**

```bash
chmod +x scripts/deploy-bepinex.sh scripts/deploy-melonloader.sh
git add scripts/
git commit -m "feat: deploy scripts for BepInEx and MelonLoader"
```

---

### C7: `dotnet new` template manifest

**Files:** Create `.template.config/template.json`

- [ ] **Step 1: Write the manifest**

```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "glockyco",
  "classifications": ["Unity", "BepInEx", "MelonLoader", "Plugin", "HotRepl"],
  "identity": "Glockyco.HotRepl.ModTemplate",
  "name": "HotRepl Mod (BepInEx + MelonLoader)",
  "shortName": "hotrepl-mod",
  "sourceName": "MyMod",
  "tags": {
    "language": "C#",
    "type": "project"
  },
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

- [ ] **Step 2: Verify install + instantiate**

```bash
dotnet new install ~/Projects/hotrepl-mod-template
dotnet new hotrepl-mod -n SmokeTest -o /tmp/SmokeTest \
  --PluginGuid smoke.test --Author "Smoke Test"
cd /tmp/SmokeTest && dotnet build src/SmokeTest.BepInEx --nologo -v q
```

Expected: clean build. (Will fail on the UnityEngine reference if `Local.props` isn't set; that's
expected — the test verifies the template parameters substituted correctly.)

- [ ] **Step 3: Commit**

```bash
cd ~/Projects/hotrepl-mod-template
git add .template.config/
git commit -m "feat: dotnet new template manifest"
```

---

### C8: CI workflow

**Files:** Create `.github/workflows/ci.yml`

- [ ] **Step 1: Write the workflow**

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.x"

      # The BepInEx project depends on UnityEngine.dll — provide a stub via
      # the BepInEx.Unity.IL2CPP package or skip BepInEx-side build on CI.
      # For now, build only the shared source folder via a fake csproj.
      - name: Verify shared source compiles
        run: |
          # Create a minimal csproj that compiles only src/MyMod/
          cat > /tmp/SourceOnly.csproj <<'XML'
          <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
              <TargetFramework>netstandard2.1</TargetFramework>
              <LangVersion>latest</LangVersion>
              <Nullable>enable</Nullable>
            </PropertyGroup>
            <ItemGroup>
              <PackageReference Include="HotRepl.Core" Version="3.0.0" />
              <Compile Include="src/MyMod/**/*.cs" />
            </ItemGroup>
          </Project>
          XML
          dotnet build /tmp/SourceOnly.csproj --nologo -v q
```

The CI verifies the shared source compiles against `HotRepl.Core`. The loader csprojs need
game-specific UnityEngine.dll references that aren't reproducible in CI without per-game licensed
binaries — they're verified manually during template instantiation by maintainers.

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: verify shared source compiles against HotRepl.Core"
```

---

### C9: README

**Files:** Create `README.md`

- [ ] **Step 1: Write the README**

````markdown
# hotrepl-mod-template

Scaffold for writing a [HotRepl](https://github.com/glockyco/HotRepl) mod for any Unity game — works
with both BepInEx (Mono) and MelonLoader (IL2CPP).

## What you're getting

A two-project mod scaffold (BepInEx + MelonLoader siblings sharing a common source folder) with one
working demo command. Drop the built DLL into your game's plugin folder, talk to the game over the
HotRepl WebSocket, get typed schema-validated responses back.

## Prerequisites

- **.NET SDK 10.x** (the projects target `netstandard2.1` for BepInEx and `net6.0` for MelonLoader).
- A game with **BepInEx 5.x** OR **MelonLoader 0.6+** installed and the HotRepl host plugin loaded
  (`HotRepl.BepInEx.dll` for BepInEx, `HotRepl.Host.MelonLoader.dll` for MelonLoader).
- The HotRepl CLI for testing: `bun add -g @hotrepl/cli` or use `bunx @hotrepl/cli`.

## Quickstart

### Option 1: Use the GitHub template

1. Click "**Use this template**" → "Create a new repository".
2. Clone your new repo locally.
3. Rename `MyMod` to your mod name throughout the project (sed or IDE-wide rename).

### Option 2: `dotnet new`

```bash
dotnet new install glockyco/hotrepl-mod-template
dotnet new hotrepl-mod -n AwesomeMod -o ~/Projects/AwesomeMod \
  --PluginGuid awesomestudios.awesomemod --Author "Awesome Studios"
```
````

### Either path: configure local paths and deploy

```bash
cp Local.props.example Local.props
# Edit Local.props and set BepInExGamePath or MelonLoaderPath etc.
./scripts/deploy-bepinex.sh    # or scripts/deploy-melonloader.sh
```

Launch your game. From any HotRepl client:

```bash
bunx @hotrepl/cli list-commands   # mymod.hello shows up
bunx @hotrepl/cli run mymod.hello '{"name":"World"}'
# → { "greeting": "Hello, World!", "generatedAt": "..." }
```

## Project layout

| Folder                   | Purpose                                                                                                                                            |
| ------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/MyMod/`             | Shared source folder — command handlers and POCO arg/result types. Compiled into both loader DLLs against their respective UnityEngine references. |
| `src/MyMod.BepInEx/`     | BepInEx loader plugin. References Mono-flavored UnityEngine.                                                                                       |
| `src/MyMod.MelonLoader/` | MelonLoader loader mod. References IL2CPP-unhollowed UnityEngine.                                                                                  |
| `scripts/`               | Build + deploy helpers.                                                                                                                            |
| `Local.props.example`    | Template for game-path configuration. Copy to `Local.props` (gitignored).                                                                          |

## Adding a new command

Copy `src/MyMod/Commands/HelloWorldCommand.cs` and the two POCO files under `src/MyMod/Models/`.
Change the names. Register the new command in `src/MyMod/MyModCatalog.cs`. Build and deploy — agents
and CLIs see your new command in `list-commands`.

## Schema authoring

Decorate POCO properties with standard .NET attributes:

| Attribute                                              | Effect                                                                                                     |
| ------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------- |
| `[Required]` (`System.ComponentModel.DataAnnotations`) | Argument is required.                                                                                      |
| `[Range(min, max)]`                                    | Numeric bounds. HotRepl validates server-side; out-of-range calls return a `validation_failed` diagnostic. |
| `[Description("…")]` (`System.ComponentModel`)         | Surface text the agent sees in `command_describe`.                                                         |
| `[JsonProperty("wireName")]` (Newtonsoft)              | Rename a property on the wire.                                                                             |
| `[JsonIgnore]`                                         | Hide a property from the schema.                                                                           |
| `[StringLength(min, max)]`                             | String length bounds.                                                                                      |

## Mono vs IL2CPP

The shared source folder works for both runtimes because the demo command only uses APIs that exist
with identical signatures on both (none from `HelloWorldCommand` — pure C#). When your command needs
Unity APIs that differ between Mono and IL2CPP (rare for inspection; common for reflection-heavy
game-type lookups), move that file out of `src/MyMod/` and into the loader-specific folder where it
compiles. The catalog adapts via `#if BEPINEX` / `#if MELONLOADER`.

## License

MIT.

````
- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: README"
````

---

### C10: Initial push

- [ ] **Step 1: Push to GitHub**

```bash
git push origin main
```

- [ ] **Step 2: Verify "Use this template" works**

Browse to `https://github.com/glockyco/hotrepl-mod-template`. Confirm the green "Use this template"
button is visible. Click it, create a test repo, clone it, confirm files are intact.

---

## Self-review

### Spec coverage

| Spec section                | Tasks                                                                                            |
| --------------------------- | ------------------------------------------------------------------------------------------------ |
| §2 Workloads                | All covered by A8 (interface) + A10 (internal context) + A13 (adapter) + B2-B5 (4 demo commands) |
| §3.1 Handler interface      | A8                                                                                               |
| §3.2 Result wrapper         | A7                                                                                               |
| §3.3 Execution context      | A9                                                                                               |
| §3.4 Registry               | A14                                                                                              |
| §4.1 Adapter                | A13                                                                                              |
| §4.2 Schema cache           | A11                                                                                              |
| §4.3 Server-side validation | A12                                                                                              |
| §5 UnityCommands plugin     | B1-B9                                                                                            |
| §6 ILRepack pipeline        | A2                                                                                               |
| §7 Configuration            | B7 (BepInEx), B8 (MelonLoader), C4 (template)                                                    |
| §8 Mod template             | C1-C10                                                                                           |
| §9 Verification             | A17 + B10                                                                                        |
| §10 Tests                   | A6, A7, A11, A12, A13, A14, A16                                                                  |
| §11 Open questions          | Defer to Phase 2 design                                                                          |

### Placeholder scan

No TBDs, no "add error handling", no "similar to Task N" — every step has either concrete code,
exact commands, or explicit reference to a prior task's output. The verification commands have
expected output.

### Type consistency

- `Version` (not `MajorVersion`) on the typed handler interface (A8) and used consistently in B/C
  tasks.
- `ControlCommandResult<TOutput>` carries `Output`, `Artifacts`, `Diagnostics`, `Succeeded` — same
  field names in A7 (definition), A13 (projection), and every command in B2-B5.
- `IControlCommandRegistry.Register<TArgs, TOutput>(...)` — same generic shape in A14 (definition),
  B6 (catalog), C3 (template catalog).
- `CompiledCommandResult` carries `Output` (JObject), `Artifacts` (list), `Diagnostics` (list),
  `Succeeded` — same shape in A10 (definition), A13 (adapter output), A15 (router consumption).
- `IArtifactWriter.WriteAsync(logicalName, bytes, contentType, ct)` — same signature in A5
  (interface), A6 (implementation), B5 (screenshot command), A13 test setup.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-24-typed-commands-phase-1-foundation.md`.

Two execution options:

1. **Subagent-Driven (recommended)** — fresh subagent per task, two-stage review (spec compliance +
   code quality) between tasks, fast iteration. Uses `superpowers:subagent-driven-development`.

2. **Inline Execution** — execute tasks in this session with batch checkpoints for review. Uses
   `superpowers:executing-plans`.

Which approach?
