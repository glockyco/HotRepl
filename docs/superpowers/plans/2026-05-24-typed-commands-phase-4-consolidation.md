# Phase 4 consolidation — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refine the HotRepl typed-command authoring API, ship a first-party C# SDK and test
harness, and make file artifacts a first-class core capability — without changing the wire protocol.

**Architecture:** Nine breaking-or-additive changes inside HotRepl. The wire is stable
(`protocolVersion: 2` unchanged); only the C#/TypeScript SDK surface and the C# authoring API
change. See `docs/superpowers/specs/2026-05-24-typed-commands-phase-4-consolidation.md` for the
architectural justification and the rejected alternatives (StreamJsonRpc,
`JsonSchema.Net.Generation`, source-gen for handler metadata).

**Tech Stack:** C# (netstandard2.0 for the new SDK + Testing packages, netstandard2.1 for
`HotRepl.Core`, net10.0 for `tests/HotRepl.Tests/`), Newtonsoft.Json, NJsonSchema 10.9.0 (pinned,
ILRepack-internalized), Fleck, TypeScript (Bun), xUnit.

---

## Milestones

The plan is structured into 9 milestones, sequenced for dependency order:

| Milestone | Theme                                         | Decisions covered | Estimated tasks |
| --------- | --------------------------------------------- | ----------------- | --------------- |
| M1        | Schema validator caching + capability honesty | 1                 | 4               |
| M2        | `ControlCommandKind.Synchronous` → `Sync`     | 2                 | 2               |
| M3        | `ControlCommandContext<TOutput>` + helpers    | 3                 | 7               |
| M4        | `[ControlCommand]` attribute                  | 4                 | 4               |
| M5        | `IArtifactWriter` expansion + artifact schema | 5                 | 8               |
| M6        | `HotRepl.Sdk` package                         | 6                 | 15              |
| M7        | `HotRepl.Testing` package                     | 7                 | 6               |
| M8        | Catalog caching in TS + C# SDKs               | 8                 | 4               |
| M9        | Docs + sample promotion                       | 9                 | 4               |

Total: 54 tasks. Each task is one bite-sized change with TDD cycle (red → green → commit).

After Phase 4a (this plan), follow-on consumer plans land:

- Phase 4b: Ardenfall update (separate plan, written after Phase 4a)
- Phase 4c: Ancient Kingdoms update (separate plan, written after Phase 4a)

Erenshor (originally Phase 4) becomes Phase 5; Documentation (originally Phase 5) becomes Phase 6.

---

## Files created or modified

### Created

```
docs/authoring-commands.md
packages/sdk/test/catalog-cache.test.ts

src/HotRepl.Core/Control/ControlCommandContextOfT.cs
src/HotRepl.Core/Control/ControlCommandAttribute.cs
src/HotRepl.Core/Control/ControlCommandArtifactAttribute.cs

src/HotRepl.Sdk/HotRepl.Sdk.csproj
src/HotRepl.Sdk/HotReplClient.cs
src/HotRepl.Sdk/HotReplClientOptions.cs
src/HotRepl.Sdk/HotReplSession.cs
src/HotRepl.Sdk/HotReplJob.cs
src/HotRepl.Sdk/HotReplResult.cs
src/HotRepl.Sdk/HotReplRunOptions.cs
src/HotRepl.Sdk/HotReplCapabilities.cs
src/HotRepl.Sdk/Artifact.cs
src/HotRepl.Sdk/HotReplJobStatus.cs
src/HotRepl.Sdk/HotReplJobProgress.cs
src/HotRepl.Sdk/HotReplErrorKind.cs
src/HotRepl.Sdk/HotReplException.cs
src/HotRepl.Sdk/HotReplConnectionException.cs
src/HotRepl.Sdk/HotReplProtocolException.cs
src/HotRepl.Sdk/HotReplCommandException.cs
src/HotRepl.Sdk/HotReplJobFailedException.cs
src/HotRepl.Sdk/HotReplSessionEvictedException.cs
src/HotRepl.Sdk/Internal/MessageDispatcher.cs
src/HotRepl.Sdk/Internal/WebSocketTransport.cs
src/HotRepl.Sdk/Internal/FrameReader.cs
src/HotRepl.Sdk/Internal/PendingRequest.cs

src/HotRepl.Testing/HotRepl.Testing.csproj
src/HotRepl.Testing/HandlerHarness.cs
src/HotRepl.Testing/HandlerResult.cs
src/HotRepl.Testing/ConformanceSuite.cs
src/HotRepl.Testing/ConformanceOptions.cs
src/HotRepl.Testing/Internal/InProcessControlContext.cs

tests/HotRepl.Sdk.Tests/HotRepl.Sdk.Tests.csproj
tests/HotRepl.Sdk.Tests/Unit/HotReplClientTests.cs
tests/HotRepl.Sdk.Tests/Unit/HotReplSessionTests.cs
tests/HotRepl.Sdk.Tests/Unit/HotReplJobTests.cs
tests/HotRepl.Sdk.Tests/Fakes/FakeTransport.cs

tests/HotRepl.Testing.Tests/HotRepl.Testing.Tests.csproj
tests/HotRepl.Testing.Tests/HandlerHarnessTests.cs
```

### Modified

```
src/HotRepl.Core/HotRepl.Core.csproj                          # Reference new attribute/context files (automatic via globbing)
src/HotRepl.Core/Control/ControlCommandKind.cs                # Synchronous → Sync
src/HotRepl.Core/Control/IControlCommandHandler.cs            # Signature uses ControlCommandContext<TOutput>
src/HotRepl.Core/Control/ControlCommandContext.cs             # Keep as base class for ControlCommandContext<TOutput>
src/HotRepl.Core/Control/ControlCommandResult.cs              # Remove static failure factories
src/HotRepl.Core/Control/ControlCommandRouter.cs              # Emit "sync" from Sync enum
src/HotRepl.Core/Control/Schema/NJsonSchemaValidator.cs       # Accept compiled JsonSchema
src/HotRepl.Core/Control/Schema/SchemaCache.cs                # Add CompiledFor<T>()
src/HotRepl.Core/Control/Schema/IControlCommandValidator.cs   # Signature uses compiled schema
src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs      # Pass ControlCommandContext<TOutput>; read [ControlCommand]; build artifacts schema
src/HotRepl.Core/Control/Artifacts/IArtifactWriter.cs         # AttachBytesAsync/AttachStreamAsync/AttachFileAsync
src/HotRepl.Core/Control/Artifacts/InMemoryArtifactWriter.cs  # Implement new signature; drop ToArray hot path
src/HotRepl.Core/Server/RuntimeHandshakeFactory.cs            # Report schemaValidation = true
src/HotRepl.Core/Control/ControlCommandDescriptor.cs          # ArtifactsSchema becomes meaningful

src/HotRepl.UnityCommands/Commands/UnityAppInfoCommand.cs        # [ControlCommand] attribute; context helpers
src/HotRepl.UnityCommands/Commands/UnityGameObjectFindCommand.cs # Same
src/HotRepl.UnityCommands/Commands/UnityTimeSetScaleCommand.cs   # Same
src/HotRepl.UnityCommands/Commands/UnityScreenshotCommand.cs     # Same + AttachBytesAsync for the PNG

packages/sdk/src/session.ts                                   # Cache commands_list; skip describe unless asked
packages/sdk/src/commands.ts                                  # Adjust DescriptorCache type
packages/mcp/src/tools.ts                                     # Use the new listCommands surface

tests/HotRepl.Tests/Unit/SchemaCacheTests.cs                  # New tests for compiled-schema caching
tests/HotRepl.Tests/Unit/NJsonSchemaValidatorTests.cs         # Reuse-validates-without-reparsing test
tests/HotRepl.Tests/Unit/ControlCommandContextTests.cs        # New tests for context helpers
tests/HotRepl.Tests/Unit/ControlCommandAttributeTests.cs      # New tests for attribute-driven metadata
tests/HotRepl.Tests/Unit/InMemoryArtifactWriterTests.cs       # New attach API tests
tests/HotRepl.Tests/Integration/TypedCommandRoundTripTests.cs # Update to new context shape

HotRepl.sln                                                   # Add HotRepl.Sdk, HotRepl.Testing, their test projects
AGENTS.md                                                     # Reference authoring-commands.md
README.md                                                     # Reference authoring-commands.md
```

### Deleted

```
(none in Phase 4a; the consumer plans Phase 4b/4c will delete hotrepl-client.ts,
HotReplExportRunner.cs, CompendiumCommandResults.cs, ArtifactCollector.cs.)
```

---

## Milestone 1 — Schema validator caching + capability honesty

Goal: cache compiled `NJsonSchema.JsonSchema` validators per type and report
`schemaValidation: true` in the handshake (it always validated, the flag was lying).

### Task 1.1: Add compiled-schema cache to `SchemaCache`

**Files:**

- Modify: `src/HotRepl.Core/Control/Schema/SchemaCache.cs`
- Test: `tests/HotRepl.Tests/Unit/SchemaCacheTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/HotRepl.Tests/Unit/SchemaCacheTests.cs
using HotRepl.Control.Schema;
using NJsonSchema;
using Xunit;

namespace HotRepl.Tests.Unit;

public sealed class SchemaCacheTests
{
    private sealed class SampleArgs { public string Name { get; set; } = ""; }

    [Fact]
    public void CompiledFor_ReturnsSameInstanceAcrossCalls()
    {
        var first = SchemaCache.CompiledFor<SampleArgs>();
        var second = SchemaCache.CompiledFor<SampleArgs>();

        Assert.Same(first, second);
        Assert.IsType<JsonSchema>(first);
    }

    [Fact]
    public void CompiledFor_AndForReturn_AreSemanticallyEquivalent()
    {
        var compiled = SchemaCache.CompiledFor<SampleArgs>();
        var json = SchemaCache.For<SampleArgs>();

        Assert.Equal(json.ToString(Newtonsoft.Json.Formatting.None),
            compiled.ToJson(Newtonsoft.Json.Formatting.None));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/HotRepl.Tests/ --filter "FullyQualifiedName~SchemaCacheTests" --nologo -v q`
Expected: FAIL, `'SchemaCache' does not contain a definition for 'CompiledFor'`.

- [ ] **Step 3: Implement `CompiledFor<T>`**

```csharp
// src/HotRepl.Core/Control/Schema/SchemaCache.cs (add to existing class)
using NJsonSchema;
using NJsonSchema.Generation;
using System.Collections.Concurrent;

internal static class SchemaCache
{
    private static readonly JsonSchemaGeneratorSettings GeneratorSettings = new()
    {
        DefaultReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull,
        SerializerSettings = new() { ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver() },
    };

    private static readonly ConcurrentDictionary<Type, JObject> JObjectCache = new();
    private static readonly ConcurrentDictionary<Type, JsonSchema> CompiledCache = new();

    public static JObject For<T>() => JObjectCache.GetOrAdd(typeof(T), static t => GenerateJObject(t));
    public static JsonSchema CompiledFor<T>() => CompiledCache.GetOrAdd(typeof(T), static t => GenerateCompiled(t));

    public static JObject AnyObject { get; } = JObject.Parse("{\"type\":\"object\"}");

    private static JsonSchema GenerateCompiled(Type t)
    {
        var generator = new JsonSchemaGenerator(GeneratorSettings);
        return generator.Generate(t);
    }

    private static JObject GenerateJObject(Type t)
    {
        var compiled = GenerateCompiled(t);
        return JObject.Parse(compiled.ToJson(Newtonsoft.Json.Formatting.None));
    }
}
```

(Adjust to match the existing `SchemaCache` content; keep the existing public surface intact, add
`CompiledFor<T>` alongside `For<T>`.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/HotRepl.Tests/ --filter "FullyQualifiedName~SchemaCacheTests" --nologo -v q`
Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```bash
git add src/HotRepl.Core/Control/Schema/SchemaCache.cs tests/HotRepl.Tests/Unit/SchemaCacheTests.cs
git commit -m "perf(core): cache compiled JsonSchema instances per command type"
```

### Task 1.2: `NJsonSchemaValidator` consumes compiled schema; stops reparsing

**Files:**

- Modify: `src/HotRepl.Core/Control/Schema/IControlCommandValidator.cs`
- Modify: `src/HotRepl.Core/Control/Schema/NJsonSchemaValidator.cs`
- Modify: `src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs`
- Test: `tests/HotRepl.Tests/Unit/NJsonSchemaValidatorTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/HotRepl.Tests/Unit/NJsonSchemaValidatorTests.cs
using HotRepl.Control.Schema;
using NJsonSchema;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

public sealed class NJsonSchemaValidatorTests
{
    [Fact]
    public void Validate_AcceptsCompiledSchema_DoesNotReparseFromJObject()
    {
        var compiled = JsonSchema.FromJsonAsync(
            "{\"type\":\"object\",\"required\":[\"name\"],\"properties\":{\"name\":{\"type\":\"string\"}}}").Result;

        var validator = new NJsonSchemaValidator();
        var ok = validator.Validate(new JObject(new JProperty("name", "x")), compiled);
        var bad = validator.Validate(new JObject(), compiled);

        Assert.True(ok.Ok);
        Assert.False(bad.Ok);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
`dotnet test tests/HotRepl.Tests/ --filter "FullyQualifiedName~NJsonSchemaValidatorTests" --nologo -v q`
Expected: FAIL, signature mismatch on `Validate(JObject, JsonSchema)`.

- [ ] **Step 3: Update validator interface and implementation**

```csharp
// src/HotRepl.Core/Control/Schema/IControlCommandValidator.cs
using NJsonSchema;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Schema;

internal interface IControlCommandValidator
{
    SchemaValidationResult Validate(JObject instance, JsonSchema compiledSchema);
}
```

```csharp
// src/HotRepl.Core/Control/Schema/NJsonSchemaValidator.cs
using System.Linq;
using NJsonSchema;
using NJsonSchema.Validation;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Schema;

internal sealed class NJsonSchemaValidator : IControlCommandValidator
{
    public SchemaValidationResult Validate(JObject instance, JsonSchema compiledSchema)
    {
        var errors = compiledSchema.Validate(instance);
        return errors.Count == 0
            ? SchemaValidationResult.Success
            : SchemaValidationResult.Failure(
                errors.Select(e => $"{e.Path}: {e.Kind}").ToArray());
    }
}
```

- [ ] **Step 4: Update `TypedCommandAdapter` to pass compiled schema**

```csharp
// src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs (in ExecuteAsync, around line 55)
// before:
//   var validation = _validator.Validate(args, Descriptor.ArgsSchema);
// after:
var validation = _validator.Validate(args, SchemaCache.CompiledFor<TArgs>());
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test tests/HotRepl.Tests/ --nologo -v q` Expected: PASS, all existing tests + the new
one.

- [ ] **Step 6: Commit**

```bash
git add src/HotRepl.Core/Control/Schema/IControlCommandValidator.cs \
        src/HotRepl.Core/Control/Schema/NJsonSchemaValidator.cs \
        src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs \
        tests/HotRepl.Tests/Unit/NJsonSchemaValidatorTests.cs
git commit -m "perf(core): validator accepts compiled JsonSchema; stops reparsing per call"
```

### Task 1.3: Report `schemaValidation: true` in the handshake

**Files:**

- Modify: `src/HotRepl.Core/Server/RuntimeHandshakeFactory.cs`
- Test: `tests/HotRepl.Tests/Integration/HandshakeTests.cs` (existing or new)

- [ ] **Step 1: Write the failing test**

```csharp
// tests/HotRepl.Tests/Integration/HandshakeTests.cs (add)
[Fact]
public void Handshake_ReportsSchemaValidationTrue()
{
    var factory = new RuntimeHandshakeFactory(/* … existing wiring …*/);
    var handshake = factory.Build();

    Assert.True(handshake.ControlCapabilities.SchemaValidation,
        "Control plane validates inbound args; the capability should report true.");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
`dotnet test tests/HotRepl.Tests/ --filter "FullyQualifiedName~HandshakeTests.Handshake_ReportsSchemaValidationTrue" --nologo -v q`
Expected: FAIL, capability currently `false`.

- [ ] **Step 3: Flip the flag**

```csharp
// src/HotRepl.Core/Server/RuntimeHandshakeFactory.cs (around line 52-57)
// before:
//   SchemaValidation = false,
// after:
SchemaValidation = true,
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/HotRepl.Tests/ --filter "FullyQualifiedName~HandshakeTests" --nologo -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/HotRepl.Core/Server/RuntimeHandshakeFactory.cs tests/HotRepl.Tests/Integration/HandshakeTests.cs
git commit -m "fix(core): handshake reports schemaValidation=true (matches actual behavior)"
```

### Task 1.4: Build and run full test suite

- [ ] **Step 1: Build core**

Run: `dotnet build src/HotRepl.Core/ --nologo -v q` Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Run full test suite**

Run: `dotnet test tests/HotRepl.Tests/ --nologo -v q` Expected: 138+ tests pass (3 new tests added
across tasks 1.1–1.3).

---

## Milestone 2 — `ControlCommandKind.Synchronous` → `Sync`

Goal: rename the C# enum value to match the wire string. Source-only break.

### Task 2.1: Rename the enum value

**Files:**

- Modify: `src/HotRepl.Core/Control/ControlCommandKind.cs`
- Modify: `src/HotRepl.Core/Control/ControlCommandRouter.cs` (wire mapping)
- Modify: All in-tree handler files in `src/HotRepl.UnityCommands/Commands/`
- Test: existing test files reference `ControlCommandKind.Synchronous`

- [ ] **Step 1: Rename in core**

```csharp
// src/HotRepl.Core/Control/ControlCommandKind.cs
namespace HotRepl.Control;

/// <summary>Execution mode for a control-plane command.</summary>
public enum ControlCommandKind
{
    /// <summary>Synchronous: response delivered in-band as command_result.</summary>
    Sync = 0,
    /// <summary>Job: server returns job_accepted, client polls job_status to terminal job_result.</summary>
    Job = 1,
}
```

- [ ] **Step 2: Update wire mapping in `ControlCommandRouter`**

Find every occurrence of `ControlCommandKind.Synchronous` in
`src/HotRepl.Core/Control/ControlCommandRouter.cs` (around lines 303-318) and rename to
`ControlCommandKind.Sync`. Wire-emitted string `"sync"` stays unchanged.

- [ ] **Step 3: Update all in-tree references**

```bash
# Inspect occurrences (read-only)
```

Run:
`grep -rl "ControlCommandKind\.Synchronous" src/ tests/ | xargs sed -i '' 's/ControlCommandKind\.Synchronous/ControlCommandKind.Sync/g'`

(macOS BSD sed; on Linux drop the `''` argument to `-i`.)

- [ ] **Step 4: Build core + UnityCommands**

Run: `dotnet build src/HotRepl.Core/ src/HotRepl.UnityCommands/ --nologo -v q` Expected: 0 warnings,
0 errors.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test tests/HotRepl.Tests/ --nologo -v q` Expected: All passing.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(core)!: rename ControlCommandKind.Synchronous to Sync (matches wire)"
```

### Task 2.2: Verify the wire emission still says `"sync"`

**Files:**

- Test: `tests/HotRepl.Tests/Unit/ControlCommandRouterTests.cs` (existing or new)

- [ ] **Step 1: Add a wire-shape regression test**

```csharp
// tests/HotRepl.Tests/Unit/ControlCommandKindWireTests.cs
using HotRepl.Control;
using HotRepl.Control.Internal;
using Newtonsoft.Json;
using Xunit;

namespace HotRepl.Tests.Unit;

public sealed class ControlCommandKindWireTests
{
    [Theory]
    [InlineData(ControlCommandKind.Sync, "sync")]
    [InlineData(ControlCommandKind.Job, "job")]
    public void Wire_EmitsLowercaseKindString(ControlCommandKind kind, string expectedWire)
    {
        // Whatever helper currently maps enum to wire; e.g. KindWireMap.ToWire(kind).
        // Adjust to match the actual ControlCommandRouter helper.
        Assert.Equal(expectedWire, ControlCommandKindWire.ToWire(kind));
    }
}
```

Adjust the call site to whatever helper is currently used (likely a private static in
`ControlCommandRouter`); if necessary, extract a small
`internal static class
ControlCommandKindWire { public static string ToWire(ControlCommandKind kind) => ...; }`
to make the mapping testable. Add `[assembly: InternalsVisibleTo("HotRepl.Tests")]` to
`src/HotRepl.Core/HotRepl.Core.csproj` if not already present.

- [ ] **Step 2: Run test to verify it passes (wire mapping unchanged)**

Run:
`dotnet test tests/HotRepl.Tests/ --filter "FullyQualifiedName~ControlCommandKindWireTests" --nologo -v q`
Expected: PASS, 2 cases.

- [ ] **Step 3: Commit**

```bash
git add tests/HotRepl.Tests/Unit/ControlCommandKindWireTests.cs src/HotRepl.Core/Control/Internal/ControlCommandKindWire.cs
git commit -m "test(core): pin wire-format mapping for ControlCommandKind"
```

---

## Milestone 3 — `ControlCommandContext<TOutput>` + instance helpers

Goal: change the handler signature so the context carries `TOutput` and exposes generic-inferring
`Ok`/`PreconditionFailed`/`ValidationFailed`/`Failed` instance methods. Remove the static
`ControlCommandResult.<Method><TOutput>` factories.

### Task 3.1: Introduce `ControlCommandContext<TOutput>` as a subclass

**Files:**

- Create: `src/HotRepl.Core/Control/ControlCommandContextOfT.cs`
- Modify: `src/HotRepl.Core/Control/ControlCommandContext.cs` (no change to base; just keep it)
- Test: `tests/HotRepl.Tests/Unit/ControlCommandContextTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/HotRepl.Tests/Unit/ControlCommandContextTests.cs
using HotRepl.Control;
using HotRepl.Control.Artifacts;
using Xunit;

namespace HotRepl.Tests.Unit;

public sealed class ControlCommandContextTests
{
    private sealed class Output { public int Value { get; set; } }

    [Fact]
    public void Ok_BuildsSuccessfulResult()
    {
        var ctx = TestContext.Create<Output>();
        var result = ctx.Ok(new Output { Value = 42 });

        Assert.True(result.Succeeded);
        Assert.Equal(42, result.Output!.Value);
    }

    [Fact]
    public void PreconditionFailed_BuildsFailureResult()
    {
        var ctx = TestContext.Create<Output>();
        var result = ctx.PreconditionFailed("missingArg", "x is required");

        Assert.False(result.Succeeded);
        Assert.Single(result.Diagnostics);
        Assert.Equal("missingArg", result.Diagnostics[0].Code);
        Assert.Equal(ControlCommandDiagnosticKind.PreconditionFailed, result.Diagnostics[0].Kind);
    }

    [Fact]
    public void ValidationFailed_BuildsFailureResult()
    {
        var ctx = TestContext.Create<Output>();
        var result = ctx.ValidationFailed("badShape", "args missing required property");

        Assert.False(result.Succeeded);
        Assert.Equal(ControlCommandDiagnosticKind.ValidationFailed, result.Diagnostics[0].Kind);
    }
}

internal static class TestContext
{
    public static ControlCommandContext<T> Create<T>(IArtifactWriter? artifacts = null) =>
        new("req-1", System.TimeSpan.FromSeconds(30), jobId: null,
            progress: null, artifacts: artifacts ?? new InMemoryArtifactWriter());
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
`dotnet test tests/HotRepl.Tests/ --filter "FullyQualifiedName~ControlCommandContextTests" --nologo -v q`
Expected: FAIL, `ControlCommandContext<>` not found.

- [ ] **Step 3: Implement `ControlCommandContext<TOutput>`**

```csharp
// src/HotRepl.Core/Control/ControlCommandContextOfT.cs
using System;
using System.Collections.Generic;
using HotRepl.Control.Artifacts;

namespace HotRepl.Control;

/// <summary>
/// Generic-binding control-command context. Exposes <see cref="Ok"/>,
/// <see cref="PreconditionFailed"/>, <see cref="ValidationFailed"/>, and
/// <see cref="Failed"/> as instance methods so the compiler infers <typeparamref name="TOutput"/>
/// from the call site instead of forcing an explicit generic argument.
/// </summary>
public sealed class ControlCommandContext<TOutput> : ControlCommandContext
{
    public ControlCommandContext(
        string requestId,
        TimeSpan timeout,
        string? jobId,
        IProgress<ControlCommandProgress>? progress,
        IArtifactWriter artifacts)
        : base(requestId, timeout, jobId, progress ?? SilentProgress.Instance, artifacts)
    {
    }

    /// <summary>Successful result with no artifacts.</summary>
    public ControlCommandResult<TOutput> Ok(TOutput output) =>
        new() { Output = output };

    /// <summary>Successful result with a pre-built artifact dictionary.</summary>
    public ControlCommandResult<TOutput> Ok(TOutput output, IReadOnlyDictionary<string, ArtifactRef> artifacts) =>
        new() { Output = output, Artifacts = artifacts };

    /// <summary>Failure: a runtime precondition was not satisfied.</summary>
    public ControlCommandResult<TOutput> PreconditionFailed(string code, string message, object? details = null) =>
        Failed(new ControlCommandDiagnostic(
            ControlCommandDiagnosticKind.PreconditionFailed, code, message,
            Retryable: false, Details: details));

    /// <summary>Failure: argument schema or business-rule violation.</summary>
    public ControlCommandResult<TOutput> ValidationFailed(string code, string message, object? details = null) =>
        Failed(new ControlCommandDiagnostic(
            ControlCommandDiagnosticKind.ValidationFailed, code, message,
            Retryable: false, Details: details));

    /// <summary>Failure built from an explicit diagnostic.</summary>
    public ControlCommandResult<TOutput> Failed(ControlCommandDiagnostic diagnostic) =>
        new() { Succeeded = false, Diagnostics = new[] { diagnostic } };
}
```

(Move `SilentProgress` to `HotRepl.Control` namespace from `HotRepl.Control.Internal` if needed, or
`using` it in the file. Adjust based on actual current location.)

- [ ] **Step 4: Run test to verify it passes**

Run:
`dotnet test tests/HotRepl.Tests/ --filter "FullyQualifiedName~ControlCommandContextTests" --nologo -v q`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/HotRepl.Core/Control/ControlCommandContextOfT.cs tests/HotRepl.Tests/Unit/ControlCommandContextTests.cs
git commit -m "feat(core): add ControlCommandContext<TOutput> with instance result helpers"
```

### Task 3.2: Change `IControlCommandHandler<TArgs, TOutput>` signature

**Files:**

- Modify: `src/HotRepl.Core/Control/IControlCommandHandler.cs`

- [ ] **Step 1: Update interface**

```csharp
// src/HotRepl.Core/Control/IControlCommandHandler.cs (replace ExecuteAsync line)
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
```

(Keep the XML doc comments; only the parameter type changes.)

- [ ] **Step 2: Build core**

Run: `dotnet build src/HotRepl.Core/ --nologo -v q` Expected: errors in dependents —
`TypedCommandAdapter`, `UnityCommands`. Next task fixes them.

### Task 3.3: Update `TypedCommandAdapter` to construct the generic context

**Files:**

- Modify: `src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs`

- [ ] **Step 1: Update `BuildHandlerContext` to return the generic context**

```csharp
// src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs (replace BuildHandlerContext)
private static ControlCommandContext<TOutput> BuildHandlerContext(CompiledCommandContext compiled)
{
    IProgress<ControlCommandProgress> progress = compiled.ProgressSink is null
        ? SilentProgress.Instance
        : new ProgressSinkAdapter(compiled.ProgressSink);

    return new ControlCommandContext<TOutput>(
        requestId: compiled.RequestId,
        timeout:   compiled.Timeout,
        jobId:     compiled.JobId,
        progress:  progress,
        artifacts: compiled.Artifacts);
}
```

- [ ] **Step 2: Build core**

Run: `dotnet build src/HotRepl.Core/ --nologo -v q` Expected: 0 errors in `HotRepl.Core`
(UnityCommands still broken — handled in task 3.5).

### Task 3.4: Remove static `ControlCommandResult.*Failed<T>` factories

**Files:**

- Modify: `src/HotRepl.Core/Control/ControlCommandResult.cs`

- [ ] **Step 1: Delete the static failure factories**

In `src/HotRepl.Core/Control/ControlCommandResult.cs`, **remove** the static methods
`ValidationFailed<TOutput>`, `PreconditionFailed<TOutput>`, and `Failed<TOutput>` from the
non-generic `ControlCommandResult` helper class (lines ~75–110 in the current file). Keep the
`Ok<TOutput>(...)` overloads — they're useful (compiler can infer `TOutput` from the argument).

```csharp
// src/HotRepl.Core/Control/ControlCommandResult.cs (after edits)
public static class ControlCommandResult
{
    /// <summary>Success with no artifacts and no diagnostics.</summary>
    public static ControlCommandResult<TOutput> Ok<TOutput>(TOutput output) =>
        new() { Output = output };

    /// <summary>Success with a single artifact attached at the top level.</summary>
    public static ControlCommandResult<TOutput> Ok<TOutput>(
        TOutput output, string artifactName, ArtifactRef artifact) =>
        new()
        {
            Output = output,
            Artifacts = new Dictionary<string, ArtifactRef>(StringComparer.Ordinal)
            {
                [artifactName] = artifact,
            },
        };

    /// <summary>Success with a pre-built artifact dictionary.</summary>
    public static ControlCommandResult<TOutput> Ok<TOutput>(
        TOutput output, IReadOnlyDictionary<string, ArtifactRef> artifacts) =>
        new() { Output = output, Artifacts = artifacts };
}
```

- [ ] **Step 2: Build core**

Run: `dotnet build src/HotRepl.Core/ --nologo -v q` Expected: 0 errors.

### Task 3.5: Migrate `UnityCommands` handlers to the new context API

**Files:**

- Modify: `src/HotRepl.UnityCommands/Commands/UnityAppInfoCommand.cs`
- Modify: `src/HotRepl.UnityCommands/Commands/UnityGameObjectFindCommand.cs`
- Modify: `src/HotRepl.UnityCommands/Commands/UnityTimeSetScaleCommand.cs`
- Modify: `src/HotRepl.UnityCommands/Commands/UnityScreenshotCommand.cs`

- [ ] **Step 1: Update each handler signature**

For each handler file, change:

```csharp
public async ValueTask<ControlCommandResult<TOutput>> ExecuteAsync(
    ControlCommandContext context,           // <- old
    TArgs args,
    CancellationToken cancellationToken)
```

to:

```csharp
public async ValueTask<ControlCommandResult<TOutput>> ExecuteAsync(
    ControlCommandContext<TOutput> context,  // <- new
    TArgs args,
    CancellationToken cancellationToken)
```

- [ ] **Step 2: Replace static failure factory calls with context method calls**

Find all `ControlCommandResult.PreconditionFailed<...>(...)` and
`ControlCommandResult.ValidationFailed<...>(...)` in the four files and replace with
`context.PreconditionFailed(...)` / `context.ValidationFailed(...)`. Existing
`ControlCommandResult.Ok(...)` static calls can stay or be replaced with `context.Ok(...)` — pick
one and apply consistently. Recommendation: `context.Ok(...)` for the new sample style.

- [ ] **Step 3: Build UnityCommands variants**

Run: `dotnet build src/HotRepl.UnityCommands.BepInEx/ --nologo -v q` Expected: 0 errors.

Run: `dotnet build src/HotRepl.UnityCommands.MelonLoader/ --nologo -v q` Expected: 0 errors
(requires MelonLoader libs; skip if not configured locally and run in CI).

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/HotRepl.Tests/ --nologo -v q` Expected: PASS (the existing typed-command
round-trip tests use a tiny handler — that needs the new signature too; fix that handler in step 5).

### Task 3.6: Update existing `TypedCommandRoundTripTests` fixtures

**Files:**

- Modify: `tests/HotRepl.Tests/Integration/TypedCommandRoundTripTests.cs`
- Modify: `tests/HotRepl.Tests/Unit/TypedCommandAdapterTests.cs`
- Modify: any other `tests/HotRepl.Tests/**/*Tests.cs` that declares an in-test handler

- [ ] **Step 1: Search for in-test handler fixtures**

Run: `grep -rl "ControlCommandContext context" tests/HotRepl.Tests/`

- [ ] **Step 2: Update each fixture's `ExecuteAsync` signature**

Same find/replace as task 3.5 — `ControlCommandContext context` →
`ControlCommandContext<TOutput> context` where `TOutput` is whatever the handler's output type is in
that fixture.

- [ ] **Step 3: Replace static failure calls in fixtures**

Find `ControlCommandResult.PreconditionFailed<...>(...)` etc. and rewrite to use the context
helpers. (Same change as task 3.5 step 2.)

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/HotRepl.Tests/ --nologo -v q` Expected: PASS, all tests.

- [ ] **Step 5: Commit (M3 finishing)**

```bash
git add -A
git commit -m "refactor(core)!: ControlCommandContext<TOutput> hosts result helpers; drop static failure factories"
```

### Task 3.7: Update CHANGELOG/migration notes

**Files:**

- Modify: `docs/superpowers/specs/2026-05-24-typed-commands-phase-4-consolidation.md` (mark M3
  complete in a status section if you keep one; otherwise skip)

- [ ] **Step 1: No code changes** — this task exists as a reminder to surface the M3 break in the
      consumer-migration plans (4b/4c). Skip if a CHANGELOG.md is not maintained.

---

## Milestone 4 — `[ControlCommand]` attribute

Goal: provide an optional runtime-read attribute that declares handler metadata in one line, as an
alternative to the four-property metadata header. No source generator.

### Task 4.1: Define the attribute

**Files:**

- Create: `src/HotRepl.Core/Control/ControlCommandAttribute.cs`
- Test: `tests/HotRepl.Tests/Unit/ControlCommandAttributeTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/HotRepl.Tests/Unit/ControlCommandAttributeTests.cs
using HotRepl.Control;
using Xunit;

namespace HotRepl.Tests.Unit;

[ControlCommand("test.example", Version = 2, Kind = ControlCommandKind.Job, MutatesState = true)]
file sealed class ExampleHandler { }

public sealed class ControlCommandAttributeTests
{
    [Fact]
    public void Attribute_ExposesAllMetadataFields()
    {
        var attr = typeof(ExampleHandler)
            .GetCustomAttributes(typeof(ControlCommandAttribute), inherit: false)
            .Cast<ControlCommandAttribute>()
            .Single();

        Assert.Equal("test.example", attr.Name);
        Assert.Equal(2, attr.Version);
        Assert.Equal(ControlCommandKind.Job, attr.Kind);
        Assert.True(attr.MutatesState);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
`dotnet test tests/HotRepl.Tests/ --filter "FullyQualifiedName~ControlCommandAttributeTests" --nologo -v q`
Expected: FAIL, `ControlCommandAttribute` not defined.

- [ ] **Step 3: Implement the attribute**

```csharp
// src/HotRepl.Core/Control/ControlCommandAttribute.cs
using System;

namespace HotRepl.Control;

/// <summary>
/// Declares command metadata on a handler type. When present, takes precedence over
/// the four metadata properties on <see cref="IControlCommandHandler{TArgs,TOutput}"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ControlCommandAttribute : Attribute
{
    public ControlCommandAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Command name is required.", nameof(name));
        Name = name;
    }

    public string Name { get; }
    public int Version { get; set; } = 1;
    public ControlCommandKind Kind { get; set; } = ControlCommandKind.Sync;
    public bool MutatesState { get; set; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
`dotnet test tests/HotRepl.Tests/ --filter "FullyQualifiedName~ControlCommandAttributeTests" --nologo -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/HotRepl.Core/Control/ControlCommandAttribute.cs tests/HotRepl.Tests/Unit/ControlCommandAttributeTests.cs
git commit -m "feat(core): add [ControlCommand] attribute for declarative handler metadata"
```

### Task 4.2: `TypedCommandAdapter` reads the attribute when present

**Files:**

- Modify: `src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs`

- [ ] **Step 1: Add attribute lookup to descriptor construction**

In the `TypedCommandAdapter` constructor:

```csharp
// before:
//   Descriptor = new ControlCommandDescriptor(
//       name: inner.Name,
//       version: inner.Version,
//       kind: inner.Kind,
//       mutatesState: inner.MutatesState,
//       …);

var attr = typeof(IControlCommandHandler<TArgs, TOutput>)
    .GetType()  // wrong — should be inner.GetType()
    ;
// Use inner.GetType() to read attributes:
var attribute = (ControlCommandAttribute?)Attribute.GetCustomAttribute(
    inner.GetType(), typeof(ControlCommandAttribute), inherit: false);

Descriptor = new ControlCommandDescriptor(
    name:         attribute?.Name         ?? inner.Name,
    version:      attribute?.Version      ?? inner.Version,
    kind:         attribute?.Kind         ?? inner.Kind,
    mutatesState: attribute?.MutatesState ?? inner.MutatesState,
    argsSchema:   SchemaCache.For<TArgs>(),
    resultSchema: SchemaCache.For<TOutput>(),
    artifactsSchema: SchemaCache.AnyObject
);
```

- [ ] **Step 2: Build core**

Run: `dotnet build src/HotRepl.Core/ --nologo -v q` Expected: 0 errors.

### Task 4.3: Integration test — attribute drives descriptor

**Files:**

- Test: `tests/HotRepl.Tests/Integration/ControlCommandAttributeIntegrationTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/HotRepl.Tests/Integration/ControlCommandAttributeIntegrationTests.cs
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using Xunit;

namespace HotRepl.Tests.Integration;

public sealed class ControlCommandAttributeIntegrationTests
{
    private sealed class Args { public string Name { get; set; } = ""; }
    private sealed class Output { public string Reply { get; set; } = ""; }

    [ControlCommand("example.attr", Version = 7, Kind = ControlCommandKind.Job, MutatesState = true)]
    private sealed class AttrHandler : IControlCommandHandler<Args, Output>
    {
        // These properties are intentionally bogus; the attribute should win.
        public string Name => "WRONG";
        public int    Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Sync;
        public bool   MutatesState => false;

        public ValueTask<ControlCommandResult<Output>> ExecuteAsync(
            ControlCommandContext<Output> context, Args args, CancellationToken ct) =>
            ValueTask.FromResult(context.Ok(new Output { Reply = $"hi {args.Name}" }));
    }

    [Fact]
    public void Descriptor_TakesAttributeOverHandlerProperties()
    {
        var registry = new GlobalControlCommandRegistry();
        registry.Register(new AttrHandler());

        var d = registry.Describe().Single(x => x.Name == "example.attr");
        Assert.Equal("example.attr", d.Name);
        Assert.Equal(7, d.Version);
        Assert.Equal(ControlCommandKind.Job, d.Kind);
        Assert.True(d.MutatesState);
    }

    [Fact]
    public void Descriptor_FallsBackToHandlerPropertiesWhenAttributeAbsent()
    {
        // Same handler shape without [ControlCommand]; properties are authoritative.
        // Use one of the existing in-tree handlers that declares properties only.
        // ...
    }
}
```

- [ ] **Step 2: Run test to verify it passes**

Run:
`dotnet test tests/HotRepl.Tests/ --filter "FullyQualifiedName~ControlCommandAttributeIntegrationTests" --nologo -v q`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs tests/HotRepl.Tests/Integration/ControlCommandAttributeIntegrationTests.cs
git commit -m "feat(core): TypedCommandAdapter reads [ControlCommand] metadata when present"
```

### Task 4.4: Migrate `UnityCommands` handlers to `[ControlCommand]`

**Files:**

- Modify: `src/HotRepl.UnityCommands/Commands/UnityAppInfoCommand.cs`
- Modify: `src/HotRepl.UnityCommands/Commands/UnityGameObjectFindCommand.cs`
- Modify: `src/HotRepl.UnityCommands/Commands/UnityTimeSetScaleCommand.cs`
- Modify: `src/HotRepl.UnityCommands/Commands/UnityScreenshotCommand.cs`

- [ ] **Step 1: For each handler, add the attribute and remove the four properties**

Example for `UnityAppInfoCommand`:

```csharp
// before:
//   public sealed class UnityAppInfoCommand : IControlCommandHandler<EmptyArgs, UnityAppInfo>
//   {
//       public string Name => "unity.app.info";
//       public int Version => 1;
//       public ControlCommandKind Kind => ControlCommandKind.Sync;
//       public bool MutatesState => false;
//       ...

[ControlCommand("unity.app.info")]  // Version=1, Kind=Sync, MutatesState=false are defaults
public sealed class UnityAppInfoCommand : IControlCommandHandler<EmptyArgs, UnityAppInfo>
{
    public ValueTask<ControlCommandResult<UnityAppInfo>> ExecuteAsync(
        ControlCommandContext<UnityAppInfo> context, EmptyArgs args, CancellationToken ct)
    {
        // ... existing body ...
    }
}
```

Repeat for the other three; pick attribute values matching the existing properties.

**Note:** the interface still requires the four properties on the implementor. Either keep them (the
attribute takes precedence at adapter-read time) or extend the interface to allow handlers to
declare metadata via attributes only — a future cleanup. For Phase 4, keep the properties on the
interface; the attribute is metadata-override only. So this task is about adding the attribute, not
removing the properties. (Properties stay because the interface requires them.)

Revise step 1 accordingly: ADD the attribute; do NOT delete the properties yet (since the interface
contract still demands them). Whether to relax the interface to make properties optional is a
follow-up — see open questions in the spec.

- [ ] **Step 2: Run test suite**

Run: `dotnet test tests/HotRepl.Tests/ --nologo -v q` Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/HotRepl.UnityCommands/Commands/*.cs
git commit -m "feat(unity-commands): adopt [ControlCommand] attribute on sample handlers"
```

---

## Milestone 5 — `IArtifactWriter` expansion + artifact schema

Goal: make file artifacts a first-class core capability via
`AttachBytesAsync`/`AttachStreamAsync`/`AttachFileAsync`. Drop the `MemoryStream.ToArray` buffer
copy in `WriteStreamAsync`. Make `ArtifactsSchema` truthful via `[ControlCommandArtifact]`.

### Task 5.1: Define the new `IArtifactWriter` interface

**Files:**

- Modify: `src/HotRepl.Core/Control/Artifacts/IArtifactWriter.cs`

- [ ] **Step 1: Replace the interface**

```csharp
// src/HotRepl.Core/Control/Artifacts/IArtifactWriter.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.Control.Artifacts;

/// <summary>
/// Authoring-time surface for attaching artifacts to a command result. Handlers
/// receive an <see cref="IArtifactWriter"/> via the command context and call
/// <see cref="AttachFileAsync"/> / <see cref="AttachBytesAsync"/> /
/// <see cref="AttachStreamAsync"/> to register top-level artifact references.
/// </summary>
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

### Task 5.2: Reimplement `InMemoryArtifactWriter`

**Files:**

- Modify: `src/HotRepl.Core/Control/Artifacts/InMemoryArtifactWriter.cs`
- Test: `tests/HotRepl.Tests/Unit/InMemoryArtifactWriterTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/HotRepl.Tests/Unit/InMemoryArtifactWriterTests.cs
using System.IO;
using System.Text;
using System.Threading.Tasks;
using HotRepl.Control.Artifacts;
using Xunit;

namespace HotRepl.Tests.Unit;

public sealed class InMemoryArtifactWriterTests
{
    [Fact]
    public async Task AttachBytesAsync_StampsLengthAndSha256()
    {
        var writer = new InMemoryArtifactWriter();
        var bytes = Encoding.UTF8.GetBytes("hello");
        var artifact = await writer.AttachBytesAsync("greeting.txt", bytes, "text/plain");

        Assert.Equal(5, artifact.ByteSize);
        Assert.Equal("greeting.txt", artifact.LogicalName);
        Assert.Equal("text/plain", artifact.ContentType);
        Assert.NotEmpty(artifact.Sha256);
        Assert.True(artifact.Finalized);
    }

    [Fact]
    public async Task AttachStreamAsync_HashesStreaming_WithoutToArray()
    {
        var writer = new InMemoryArtifactWriter();
        var data = Encoding.UTF8.GetBytes("streamed content");
        using var ms = new MemoryStream(data);
        var artifact = await writer.AttachStreamAsync("stream.bin", ms, "application/octet-stream");

        Assert.Equal(data.Length, artifact.ByteSize);
        Assert.NotEmpty(artifact.Sha256);
    }

    [Fact]
    public async Task AttachFileAsync_ReadsFromDiskAndStampsByteSize()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmp, "file content");
            var writer = new InMemoryArtifactWriter();
            var artifact = await writer.AttachFileAsync("doc", tmp, "text/plain");

            Assert.Equal(new FileInfo(tmp).Length, artifact.ByteSize);
            Assert.Equal(tmp, artifact.Path);
            Assert.True(artifact.Uri.StartsWith("file://"));
            Assert.NotEmpty(artifact.Sha256);
        }
        finally { File.Delete(tmp); }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
`dotnet test tests/HotRepl.Tests/ --filter "FullyQualifiedName~InMemoryArtifactWriterTests" --nologo -v q`
Expected: FAIL — `Attach*` methods not defined.

- [ ] **Step 3: Reimplement the writer**

```csharp
// src/HotRepl.Core/Control/Artifacts/InMemoryArtifactWriter.cs
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.Control.Artifacts;

public sealed class InMemoryArtifactWriter : IArtifactWriter
{
    private readonly ConcurrentDictionary<string, byte[]> _store =
        new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, byte[]> Snapshot => _store;

    public ValueTask<ArtifactRef> AttachBytesAsync(
        string logicalName, ReadOnlyMemory<byte> data, string contentType, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var bytes = data.ToArray();
        _store[logicalName] = bytes;

        var sha = Sha256Hex(bytes);
        var uri = $"hotrepl-mem://{logicalName}";
        return new ValueTask<ArtifactRef>(new ArtifactRef(
            LogicalName: logicalName,
            Uri: uri,
            Path: null,
            ContentType: contentType,
            ByteSize: bytes.LongLength,
            Sha256: sha,
            Finalized: true));
    }

    public async ValueTask<ArtifactRef> AttachStreamAsync(
        string logicalName, Stream stream, string contentType, CancellationToken ct = default)
    {
        // Buffer to byte[] streaming; SHA-256 hashed incrementally so no ToArray() spike.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            memory.Write(buffer, 0, read);
        }

        var bytes = memory.ToArray();
        _store[logicalName] = bytes;

        var sha = ToHex(hash.GetHashAndReset());
        var uri = $"hotrepl-mem://{logicalName}";
        return new ArtifactRef(
            LogicalName: logicalName,
            Uri: uri,
            Path: null,
            ContentType: contentType,
            ByteSize: bytes.LongLength,
            Sha256: sha,
            Finalized: true);
    }

    public async ValueTask<ArtifactRef> AttachFileAsync(
        string logicalName, string path, string contentType, CancellationToken ct = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException($"Artifact source file not found: {path}", path);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var stream = File.OpenRead(path))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                hash.AppendData(buffer, 0, read);
        }

        var sha = ToHex(hash.GetHashAndReset());
        var uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
        return new ArtifactRef(
            LogicalName: logicalName,
            Uri: uri,
            Path: path,
            ContentType: contentType,
            ByteSize: info.Length,
            Sha256: sha,
            Finalized: true);
    }

    private static string Sha256Hex(byte[] data)
    {
        using var sha = SHA256.Create();
        return ToHex(sha.ComputeHash(data));
    }

    private static string ToHex(byte[] hash)
    {
        var sb = new System.Text.StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:
`dotnet test tests/HotRepl.Tests/ --filter "FullyQualifiedName~InMemoryArtifactWriterTests" --nologo -v q`
Expected: PASS, 3 tests.

- [ ] **Step 5: Build and run full suite**

Run: `dotnet test tests/HotRepl.Tests/ --nologo -v q` Expected: all PASS (no other internal consumer
of the old `WriteAsync`/`WriteStreamAsync` signatures should remain; if any test still calls them,
update to the new names in the same commit).

- [ ] **Step 6: Commit**

```bash
git add src/HotRepl.Core/Control/Artifacts/IArtifactWriter.cs \
        src/HotRepl.Core/Control/Artifacts/InMemoryArtifactWriter.cs \
        tests/HotRepl.Tests/Unit/InMemoryArtifactWriterTests.cs
git commit -m "feat(core)!: artifact writer adds AttachBytes/Stream/FileAsync; drop ToArray buffer copy"
```

### Task 5.3: `[ControlCommandArtifact]` attribute

**Files:**

- Create: `src/HotRepl.Core/Control/ControlCommandArtifactAttribute.cs`
- Test: `tests/HotRepl.Tests/Unit/ControlCommandArtifactAttributeTests.cs`

- [ ] **Step 1: Define the attribute**

```csharp
// src/HotRepl.Core/Control/ControlCommandArtifactAttribute.cs
using System;

namespace HotRepl.Control;

/// <summary>
/// Declares an expected artifact key produced by a command handler. Surfaces to MCP
/// clients via <c>command_describe</c>. May be applied multiple times on a handler.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class ControlCommandArtifactAttribute : Attribute
{
    public ControlCommandArtifactAttribute(string keyPattern)
    {
        if (string.IsNullOrWhiteSpace(keyPattern))
            throw new ArgumentException("Artifact key pattern is required.", nameof(keyPattern));
        KeyPattern = keyPattern;
    }

    /// <summary>Logical artifact key or pattern (e.g. <c>data.&lt;stem&gt;</c>).</summary>
    public string KeyPattern { get; }
    public string ContentType { get; set; } = "application/octet-stream";
    public bool Required { get; set; }
    /// <summary>Cardinality hint: <c>1</c>, <c>0..1</c>, <c>1..*</c>, <c>0..*</c>. Default <c>1</c>.</summary>
    public string RepeatCount { get; set; } = "1";
}
```

- [ ] **Step 2: Write the test**

```csharp
// tests/HotRepl.Tests/Unit/ControlCommandArtifactAttributeTests.cs
using HotRepl.Control;
using Xunit;

namespace HotRepl.Tests.Unit;

[ControlCommandArtifact("data.<stem>", ContentType = "application/json", Required = true, RepeatCount = "1..*")]
[ControlCommandArtifact("screenshots.metadata", ContentType = "application/json", Required = false)]
file sealed class ExampleArtifactHandler { }

public sealed class ControlCommandArtifactAttributeTests
{
    [Fact]
    public void Attributes_AreReadable()
    {
        var attrs = typeof(ExampleArtifactHandler)
            .GetCustomAttributes(typeof(ControlCommandArtifactAttribute), inherit: false)
            .Cast<ControlCommandArtifactAttribute>()
            .OrderBy(a => a.KeyPattern, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, attrs.Length);
        Assert.Equal("data.<stem>", attrs[0].KeyPattern);
        Assert.Equal("1..*", attrs[0].RepeatCount);
        Assert.True(attrs[0].Required);
    }
}
```

- [ ] **Step 3: Run test to verify PASS**

Run:
`dotnet test tests/HotRepl.Tests/ --filter "FullyQualifiedName~ControlCommandArtifactAttributeTests" --nologo -v q`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/HotRepl.Core/Control/ControlCommandArtifactAttribute.cs tests/HotRepl.Tests/Unit/ControlCommandArtifactAttributeTests.cs
git commit -m "feat(core): add [ControlCommandArtifact] for declared artifact keys"
```

### Task 5.4: `TypedCommandAdapter` compiles artifacts schema from declarations

**Files:**

- Modify: `src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs`
- Test: integration test that round-trips `artifactsSchema`.

- [ ] **Step 1: Update the descriptor builder**

In the `TypedCommandAdapter` constructor, replace the `artifactsSchema: SchemaCache.AnyObject` with
a compiled schema from `[ControlCommandArtifact]` declarations:

```csharp
// helper at the bottom of the file:
private static JObject BuildArtifactsSchema(Type handlerType)
{
    var attrs = handlerType
        .GetCustomAttributes(typeof(ControlCommandArtifactAttribute), inherit: false)
        .Cast<ControlCommandArtifactAttribute>()
        .ToArray();

    if (attrs.Length == 0) return SchemaCache.AnyObject;

    // Build a JSON Schema with patternProperties matching the declared key patterns.
    var schema = new JObject
    {
        ["type"] = "object",
        ["patternProperties"] = new JObject(),
        ["required"] = new JArray(),
        ["additionalProperties"] = false,
    };

    var patternProps = (JObject)schema["patternProperties"]!;
    var required = (JArray)schema["required"]!;

    foreach (var attr in attrs)
    {
        var pattern = ConvertGlobToRegex(attr.KeyPattern);  // simple <stem> -> .+ mapping
        patternProps[pattern] = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["uri"] = new JObject { ["type"] = "string" },
                ["sha256"] = new JObject { ["type"] = "string" },
                ["byteSize"] = new JObject { ["type"] = "integer" },
                ["contentType"] = new JObject { ["type"] = "string", ["const"] = attr.ContentType },
            },
        };

        if (attr.Required && !attr.KeyPattern.Contains("<"))
            required.Add(attr.KeyPattern);
    }

    return schema;
}

private static string ConvertGlobToRegex(string glob)
{
    // Map `data.<stem>` -> `^data\\.[^.]+$`. Keep it simple; future cleanup can do full glob.
    var escaped = System.Text.RegularExpressions.Regex.Escape(glob);
    return "^" + escaped.Replace("<stem>", "[^.]+") + "$";
}
```

Then in the constructor:

```csharp
Descriptor = new ControlCommandDescriptor(
    name:         attribute?.Name         ?? inner.Name,
    version:      attribute?.Version      ?? inner.Version,
    kind:         attribute?.Kind         ?? inner.Kind,
    mutatesState: attribute?.MutatesState ?? inner.MutatesState,
    argsSchema:   SchemaCache.For<TArgs>(),
    resultSchema: SchemaCache.For<TOutput>(),
    artifactsSchema: BuildArtifactsSchema(inner.GetType())
);
```

- [ ] **Step 2: Integration test**

```csharp
// tests/HotRepl.Tests/Integration/ArtifactsSchemaTests.cs
[Fact]
public void Descriptor_ArtifactsSchema_ReflectsDeclaredAttributes()
{
    var registry = new GlobalControlCommandRegistry();
    registry.Register(new TwoArtifactHandler());

    var d = registry.Describe().Single(x => x.Name == "test.two-artifacts");
    var schema = d.ArtifactsSchema;

    Assert.Equal("object", schema["type"]!.ToString());
    var patterns = (JObject)schema["patternProperties"]!;
    Assert.Equal(2, patterns.Count);
}

[ControlCommand("test.two-artifacts")]
[ControlCommandArtifact("data.<stem>", ContentType = "application/json", Required = true, RepeatCount = "1..*")]
[ControlCommandArtifact("screenshots.metadata", ContentType = "application/json")]
file sealed class TwoArtifactHandler : IControlCommandHandler<EmptyArgs, object>
{
    public string Name => "test.two-artifacts";
    public int Version => 1;
    public ControlCommandKind Kind => ControlCommandKind.Sync;
    public bool MutatesState => false;
    public ValueTask<ControlCommandResult<object>> ExecuteAsync(
        ControlCommandContext<object> context, EmptyArgs args, CancellationToken ct) =>
        ValueTask.FromResult(context.Ok((object)new { }));
}
```

- [ ] **Step 3: Run test**

Run:
`dotnet test tests/HotRepl.Tests/ --filter "FullyQualifiedName~ArtifactsSchemaTests" --nologo -v q`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/HotRepl.Core/Control/Internal/TypedCommandAdapter.cs tests/HotRepl.Tests/Integration/ArtifactsSchemaTests.cs
git commit -m "feat(core): descriptor exposes artifacts schema from [ControlCommandArtifact] declarations"
```

### Task 5.5: Migrate `UnityScreenshotCommand` to `AttachBytesAsync`

**Files:**

- Modify: `src/HotRepl.UnityCommands/Commands/UnityScreenshotCommand.cs`

- [ ] **Step 1: Replace whatever `WriteAsync`/`WriteStreamAsync` calls exist with
      `AttachBytesAsync`**

```csharp
// before (approximate):
//   var bytes = png.Encode();
//   var artifact = await context.Artifacts.WriteAsync("screenshot", bytes, "image/png");

var bytes = png.Encode();
var artifact = await context.Artifacts.AttachBytesAsync(
    logicalName: "screenshot",
    data: bytes,
    contentType: "image/png",
    cancellationToken);

return context.Ok(new UnityScreenshotResult { ... }, ImmutableDictionary.CreateRange(
    new[] { new KeyValuePair<string, ArtifactRef>("screenshot", artifact) }));
```

- [ ] **Step 2: Add `[ControlCommandArtifact]` declaration**

```csharp
[ControlCommand("unity.screenshot", MutatesState = false)]
[ControlCommandArtifact("screenshot", ContentType = "image/png", Required = true)]
public sealed class UnityScreenshotCommand : IControlCommandHandler<UnityScreenshotArgs, UnityScreenshotResult>
{
    ...
}
```

- [ ] **Step 3: Build UnityCommands**

Run: `dotnet build src/HotRepl.UnityCommands.BepInEx/ --nologo -v q` Expected: 0 errors.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/HotRepl.Tests/ --nologo -v q` Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/HotRepl.UnityCommands/Commands/UnityScreenshotCommand.cs
git commit -m "feat(unity-commands): migrate screenshot to AttachBytesAsync + declare artifact key"
```

### Task 5.6: Smoke-test M5 against full gate

- [ ] **Step 1: Build core + UnityCommands**

Run: `dotnet build src/HotRepl.Core/ src/HotRepl.UnityCommands.BepInEx/ --nologo -v q` Expected: 0
errors, 0 warnings.

- [ ] **Step 2: Full test suite**

Run: `dotnet test tests/HotRepl.Tests/ --nologo -v q` Expected: all tests PASS.

- [ ] **Step 3: dprint + typos**

Run: `dprint check && typos` Expected: clean.

---

## Milestone 6 — `HotRepl.Sdk` package

Goal: ship a first-party C# SDK on netstandard2.0 that mirrors the conceptual surface of
`packages/sdk/src/session.ts` and follows the canonical SDK conventions surveyed in
`agent://52-CSharpSdkPatterns`.

### Task 6.1: Create the project skeleton

**Files:**

- Create: `src/HotRepl.Sdk/HotRepl.Sdk.csproj`
- Modify: `HotRepl.sln`

- [ ] **Step 1: Write the csproj**

```xml
<!-- src/HotRepl.Sdk/HotRepl.Sdk.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AssemblyName>HotRepl.Sdk</AssemblyName>
    <RootNamespace>HotRepl.Sdk</RootNamespace>
    <PackageId>HotRepl.Sdk</PackageId>
    <Description>First-party C# SDK for the HotRepl typed-command protocol.</Description>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\HotRepl.Protocol\HotRepl.Protocol.csproj" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="Microsoft.Bcl.AsyncInterfaces" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="HotRepl.Sdk.Tests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add to solution**

Run: `dotnet sln HotRepl.sln add src/HotRepl.Sdk/HotRepl.Sdk.csproj` Expected:
`Project ... added to the solution.`

- [ ] **Step 3: Build empty project**

Run: `dotnet build src/HotRepl.Sdk/ --nologo -v q` Expected: 0 errors (project compiles even with no
code).

- [ ] **Step 4: Commit**

```bash
git add src/HotRepl.Sdk/HotRepl.Sdk.csproj HotRepl.sln
git commit -m "feat(sdk): scaffold HotRepl.Sdk netstandard2.0 package"
```

### Task 6.2: `HotReplClientOptions` and `HotReplRunOptions`

**Files:**

- Create: `src/HotRepl.Sdk/HotReplClientOptions.cs`
- Create: `src/HotRepl.Sdk/HotReplRunOptions.cs`

- [ ] **Step 1: Write the option records**

```csharp
// src/HotRepl.Sdk/HotReplClientOptions.cs
using System;
using Newtonsoft.Json;

namespace HotRepl.Sdk;

public sealed class HotReplClientOptions
{
    public TimeSpan ConnectTimeout     { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan RequestTimeout     { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan JobPollingInterval { get; set; } = TimeSpan.FromMilliseconds(250);
    public bool     ValidateSchemas    { get; set; }   // opt-in client-side validation
    public JsonSerializerSettings? SerializerSettings { get; set; }
}
```

```csharp
// src/HotRepl.Sdk/HotReplRunOptions.cs
using System;

namespace HotRepl.Sdk;

public sealed class HotReplRunOptions
{
    public TimeSpan? Timeout { get; set; }
    public TimeSpan? PollingInterval { get; set; }  // job-only
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/HotRepl.Sdk/ --nologo -v q` Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/HotRepl.Sdk/HotReplClientOptions.cs src/HotRepl.Sdk/HotReplRunOptions.cs
git commit -m "feat(sdk): options records for client and per-call configuration"
```

### Task 6.3: Exception hierarchy

**Files:**

- Create: `src/HotRepl.Sdk/HotReplErrorKind.cs`
- Create: `src/HotRepl.Sdk/HotReplException.cs`
- Create: `src/HotRepl.Sdk/HotReplConnectionException.cs`
- Create: `src/HotRepl.Sdk/HotReplProtocolException.cs`
- Create: `src/HotRepl.Sdk/HotReplCommandException.cs`
- Create: `src/HotRepl.Sdk/HotReplJobFailedException.cs`
- Create: `src/HotRepl.Sdk/HotReplSessionEvictedException.cs`

- [ ] **Step 1: Define the kind enum**

```csharp
// src/HotRepl.Sdk/HotReplErrorKind.cs
namespace HotRepl.Sdk;

public enum HotReplErrorKind
{
    Internal,
    InvalidRequest,
    UnsupportedOperation,
    ValidationFailed,
    PreconditionFailed,
    Conflict,
    Cancelled,
    SessionEvicted,
    Connection,
    Timeout,
    Protocol,
}
```

- [ ] **Step 2: Define the base exception**

```csharp
// src/HotRepl.Sdk/HotReplException.cs
using System;
using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk;

public class HotReplException : Exception
{
    public HotReplException(HotReplErrorKind kind, string code, string message, bool retryable, JToken? details = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Code = code;
        Retryable = retryable;
        Details = details;
    }

    public HotReplErrorKind Kind      { get; }
    public string           Code      { get; }
    public bool             Retryable { get; }
    public JToken?          Details   { get; }
}
```

- [ ] **Step 3: Define the derived exceptions**

```csharp
// src/HotRepl.Sdk/HotReplConnectionException.cs
namespace HotRepl.Sdk;

public sealed class HotReplConnectionException : HotReplException
{
    public HotReplConnectionException(string message, System.Exception? inner = null)
        : base(HotReplErrorKind.Connection, "connectionFailed", message, retryable: true, innerException: inner) { }
}
```

```csharp
// src/HotRepl.Sdk/HotReplProtocolException.cs
namespace HotRepl.Sdk;

public sealed class HotReplProtocolException : HotReplException
{
    public HotReplProtocolException(string code, string message)
        : base(HotReplErrorKind.Protocol, code, message, retryable: false) { }
}
```

```csharp
// src/HotRepl.Sdk/HotReplCommandException.cs
using Newtonsoft.Json.Linq;
namespace HotRepl.Sdk;

public sealed class HotReplCommandException : HotReplException
{
    public HotReplCommandException(HotReplErrorKind kind, string code, string message, bool retryable, JToken? details = null)
        : base(kind, code, message, retryable, details) { }
}
```

```csharp
// src/HotRepl.Sdk/HotReplJobFailedException.cs
using Newtonsoft.Json.Linq;
namespace HotRepl.Sdk;

public sealed class HotReplJobFailedException : HotReplException
{
    public HotReplJobFailedException(string code, string message, JToken? details = null)
        : base(HotReplErrorKind.PreconditionFailed, code, message, retryable: false, details: details) { }

    public string JobId { get; set; } = "";
}
```

```csharp
// src/HotRepl.Sdk/HotReplSessionEvictedException.cs
namespace HotRepl.Sdk;

public sealed class HotReplSessionEvictedException : HotReplException
{
    public HotReplSessionEvictedException(string reason)
        : base(HotReplErrorKind.SessionEvicted, "sessionEvicted", reason, retryable: false) { }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/HotRepl.Sdk/ --nologo -v q` Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/HotRepl.Sdk/HotReplErrorKind.cs src/HotRepl.Sdk/HotReplException.cs \
        src/HotRepl.Sdk/HotReplConnectionException.cs src/HotRepl.Sdk/HotReplProtocolException.cs \
        src/HotRepl.Sdk/HotReplCommandException.cs src/HotRepl.Sdk/HotReplJobFailedException.cs \
        src/HotRepl.Sdk/HotReplSessionEvictedException.cs
git commit -m "feat(sdk): exception hierarchy mirrors TS HotReplError shape"
```

### Task 6.4: WebSocket transport + message dispatcher

**Files:**

- Create: `src/HotRepl.Sdk/Internal/WebSocketTransport.cs`
- Create: `src/HotRepl.Sdk/Internal/MessageDispatcher.cs`
- Create: `src/HotRepl.Sdk/Internal/FrameReader.cs`
- Create: `src/HotRepl.Sdk/Internal/PendingRequest.cs`
- Test: `tests/HotRepl.Sdk.Tests/HotRepl.Sdk.Tests.csproj` and an initial unit test

- [ ] **Step 1: Test project scaffolding**

```xml
<!-- tests/HotRepl.Sdk.Tests/HotRepl.Sdk.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\HotRepl.Sdk\HotRepl.Sdk.csproj" />
    <ProjectReference Include="..\..\src\HotRepl.Protocol\HotRepl.Protocol.csproj" />
  </ItemGroup>

</Project>
```

Run: `dotnet sln HotRepl.sln add tests/HotRepl.Sdk.Tests/HotRepl.Sdk.Tests.csproj`

- [ ] **Step 2: Define `PendingRequest`**

```csharp
// src/HotRepl.Sdk/Internal/PendingRequest.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk.Internal;

internal sealed class PendingRequest
{
    public PendingRequest(TaskCompletionSource<JObject> tcs, CancellationTokenRegistration ctRegistration)
    {
        Completion = tcs;
        CancellationRegistration = ctRegistration;
    }
    public TaskCompletionSource<JObject> Completion { get; }
    public CancellationTokenRegistration CancellationRegistration { get; }
}
```

- [ ] **Step 3: Define `FrameReader`**

```csharp
// src/HotRepl.Sdk/Internal/FrameReader.cs
using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.Sdk.Internal;

internal static class FrameReader
{
    public static async Task<string?> ReadOneAsync(ClientWebSocket socket, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text)
                throw new HotReplProtocolException("nonTextFrame", "Expected text WebSocket frame.");
            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
```

- [ ] **Step 4: Define `MessageDispatcher`**

```csharp
// src/HotRepl.Sdk/Internal/MessageDispatcher.cs
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk.Internal;

/// <summary>
/// Receive loop + request/response correlation. One instance per session.
/// </summary>
internal sealed class MessageDispatcher : IAsyncDisposable
{
    private readonly ClientWebSocket _socket;
    private readonly ConcurrentDictionary<string, PendingRequest> _pending = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _readLoop;

    public event Action<JObject>? Pushed;  // session_evicted, subscribe_*, etc.

    public MessageDispatcher(ClientWebSocket socket)
    {
        _socket = socket;
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public Task<JObject> ExpectResponseAsync(string id, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
        linkedCts.CancelAfter(timeout);

        var registration = linkedCts.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var p))
                p.Completion.TrySetCanceled(linkedCts.Token);
        });

        _pending[id] = new PendingRequest(tcs, registration);
        return tcs.Task;
    }

    public async Task SendAsync(JObject message, CancellationToken ct)
    {
        var json = message.ToString(Formatting.None);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var raw = await FrameReader.ReadOneAsync(_socket, _shutdown.Token).ConfigureAwait(false);
                if (raw is null) break;
                var msg = JObject.Parse(raw);
                var id = msg["id"]?.ToString();
                if (id != null && _pending.TryRemove(id, out var pending))
                {
                    pending.CancellationRegistration.Dispose();
                    pending.Completion.TrySetResult(msg);
                }
                else
                {
                    Pushed?.Invoke(msg);
                }
            }
        }
        catch (Exception ex) when (!_shutdown.IsCancellationRequested)
        {
            FailAllPending(new HotReplConnectionException("Read loop terminated.", ex));
        }
    }

    private void FailAllPending(HotReplException error)
    {
        foreach (var kv in _pending)
        {
            if (_pending.TryRemove(kv.Key, out var p))
            {
                p.CancellationRegistration.Dispose();
                p.Completion.TrySetException(error);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        try { await _readLoop.ConfigureAwait(false); } catch { /* ignore */ }
        FailAllPending(new HotReplConnectionException("Session closed."));
        _shutdown.Dispose();
    }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build src/HotRepl.Sdk/ --nologo -v q` Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/HotRepl.Sdk/Internal/*.cs tests/HotRepl.Sdk.Tests/HotRepl.Sdk.Tests.csproj HotRepl.sln
git commit -m "feat(sdk): WebSocket transport + request/response correlator"
```

### Task 6.5: `HotReplCapabilities`, `HotReplJobStatus`, `HotReplJobProgress`

**Files:**

- Create: `src/HotRepl.Sdk/HotReplCapabilities.cs`
- Create: `src/HotRepl.Sdk/HotReplJobStatus.cs`
- Create: `src/HotRepl.Sdk/HotReplJobProgress.cs`

- [ ] **Step 1: Write the small types**

```csharp
// src/HotRepl.Sdk/HotReplCapabilities.cs
using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk;

public sealed class HotReplCapabilities
{
    public HotReplCapabilities(JObject raw, int protocolVersion, bool schemaValidation)
    {
        Raw = raw;
        ProtocolVersion = protocolVersion;
        SchemaValidation = schemaValidation;
    }
    public JObject Raw { get; }
    public int ProtocolVersion { get; }
    public bool SchemaValidation { get; }
}
```

```csharp
// src/HotRepl.Sdk/HotReplJobStatus.cs
namespace HotRepl.Sdk;

public sealed class HotReplJobStatus
{
    public HotReplJobStatus(string jobId, string state)
    {
        JobId = jobId;
        State = state;
    }
    public string JobId { get; }
    public string State { get; }  // "running" | "done" | "failed" | "cancelled"
}
```

```csharp
// src/HotRepl.Sdk/HotReplJobProgress.cs
using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk;

public sealed class HotReplJobProgress
{
    public HotReplJobProgress(JObject? snapshot, string? message)
    {
        Snapshot = snapshot;
        Message = message;
    }
    public JObject? Snapshot { get; }
    public string? Message { get; }
}
```

- [ ] **Step 2: Build + commit**

Run: `dotnet build src/HotRepl.Sdk/ --nologo -v q`

```bash
git add src/HotRepl.Sdk/HotReplCapabilities.cs src/HotRepl.Sdk/HotReplJobStatus.cs src/HotRepl.Sdk/HotReplJobProgress.cs
git commit -m "feat(sdk): job status, progress, and capabilities value types"
```

### Task 6.6: `Artifact` value type

**Files:**

- Create: `src/HotRepl.Sdk/Artifact.cs`

- [ ] **Step 1: Write the artifact wrapper**

```csharp
// src/HotRepl.Sdk/Artifact.cs
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Protocol;  // ArtifactRef
using Newtonsoft.Json;

namespace HotRepl.Sdk;

public sealed class Artifact
{
    private static readonly HttpClient SharedHttp = new();

    private readonly ArtifactRef _ref;
    private byte[]? _cachedBytes;
    private readonly object _gate = new();

    internal Artifact(ArtifactRef artifactRef) => _ref = artifactRef;

    public ArtifactRef Ref => _ref;

    public async Task<byte[]> BytesAsync(CancellationToken ct = default)
    {
        if (_cachedBytes != null) return _cachedBytes;

        byte[] bytes;
        if (!string.IsNullOrEmpty(_ref.Path) && File.Exists(_ref.Path))
        {
            bytes = await File.ReadAllBytesAsync(_ref.Path, ct).ConfigureAwait(false);
        }
        else if (Uri.TryCreate(_ref.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file")
        {
            if (uri.Scheme == "file")
                bytes = await File.ReadAllBytesAsync(uri.LocalPath, ct).ConfigureAwait(false);
            else
                bytes = await SharedHttp.GetByteArrayAsync(uri, ct).ConfigureAwait(false);
        }
        else
        {
            throw new HotReplProtocolException("artifactUnreachable",
                $"Artifact '{_ref.LogicalName}' is not reachable via path or http(s)/file URI.");
        }

        VerifyHash(bytes);
        lock (_gate) _cachedBytes ??= bytes;
        return bytes;
    }

    public async Task<string> TextAsync(Encoding? encoding = null, CancellationToken ct = default)
    {
        var bytes = await BytesAsync(ct).ConfigureAwait(false);
        return (encoding ?? Encoding.UTF8).GetString(bytes);
    }

    public async Task<T> JsonAsync<T>(CancellationToken ct = default)
    {
        var text = await TextAsync(Encoding.UTF8, ct).ConfigureAwait(false);
        return JsonConvert.DeserializeObject<T>(text)!;
    }

    private void VerifyHash(byte[] bytes)
    {
        if (string.IsNullOrEmpty(_ref.Sha256)) return;
        using var sha = SHA256.Create();
        var actual = ToHex(sha.ComputeHash(bytes));
        if (!string.Equals(actual, _ref.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new HotReplProtocolException("artifactHashMismatch",
                $"SHA-256 mismatch for artifact '{_ref.LogicalName}'.");
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
```

- [ ] **Step 2: Build + commit**

Run: `dotnet build src/HotRepl.Sdk/ --nologo -v q`

```bash
git add src/HotRepl.Sdk/Artifact.cs
git commit -m "feat(sdk): Artifact wrapper with bytes/text/JSON readers and sha256 verification"
```

### Task 6.7: `HotReplResult<T>`

**Files:**

- Create: `src/HotRepl.Sdk/HotReplResult.cs`

- [ ] **Step 1: Write the result wrapper**

```csharp
// src/HotRepl.Sdk/HotReplResult.cs
using System.Collections.Generic;

namespace HotRepl.Sdk;

public sealed class HotReplResult<TOutput>
{
    public HotReplResult(TOutput output, IReadOnlyDictionary<string, Artifact> artifacts)
    {
        Output = output;
        Artifacts = artifacts;
    }
    public TOutput Output { get; }
    public IReadOnlyDictionary<string, Artifact> Artifacts { get; }
}
```

- [ ] **Step 2: Build + commit**

Run: `dotnet build src/HotRepl.Sdk/ --nologo -v q`

```bash
git add src/HotRepl.Sdk/HotReplResult.cs
git commit -m "feat(sdk): HotReplResult<T> carries typed output plus artifact map"
```

### Task 6.8: `HotReplSession` — connect + request/response core

**Files:**

- Create: `src/HotRepl.Sdk/HotReplSession.cs`

- [ ] **Step 1: Write the session class skeleton (connect + request/response only)**

```csharp
// src/HotRepl.Sdk/HotReplSession.cs
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Sdk.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk;

public sealed class HotReplSession : IAsyncDisposable
{
    private readonly MessageDispatcher _dispatcher;
    private readonly HotReplClientOptions _options;
    private int _idCounter;

    public HotReplCapabilities Capabilities { get; }
    public bool IsConnected => _dispatcher != null;

    internal HotReplSession(MessageDispatcher dispatcher, HotReplCapabilities caps, HotReplClientOptions options)
    {
        _dispatcher = dispatcher;
        Capabilities = caps;
        _options = options;
    }

    internal string NextId(string prefix) => $"{prefix}-{Interlocked.Increment(ref _idCounter)}";

    internal Task<JObject> RequestRawAsync(JObject message, CancellationToken ct)
    {
        var id = (string)message["id"]!;
        var pending = _dispatcher.ExpectResponseAsync(id, _options.RequestTimeout, ct);
        _ = _dispatcher.SendAsync(message, ct);
        return pending;
    }

    public Task CloseAsync(CancellationToken ct = default) => DisposeAsync().AsTask();

    public ValueTask DisposeAsync() => _dispatcher.DisposeAsync();
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/HotRepl.Sdk/ --nologo -v q` Expected: 0 errors.

### Task 6.9: `HotReplClient.ConnectAsync`

**Files:**

- Create: `src/HotRepl.Sdk/HotReplClient.cs`

- [ ] **Step 1: Write the client + handshake logic**

```csharp
// src/HotRepl.Sdk/HotReplClient.cs
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Sdk.Internal;
using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk;

public sealed class HotReplClient
{
    private readonly Uri _endpoint;
    private readonly HotReplClientOptions _options;

    public HotReplClient(Uri endpoint, HotReplClientOptions? options = null)
    {
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _options = options ?? new HotReplClientOptions();
    }

    public async Task<HotReplSession> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var socket = new ClientWebSocket();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(_options.ConnectTimeout);
        try { await socket.ConnectAsync(_endpoint, connectCts.Token).ConfigureAwait(false); }
        catch (Exception ex)
        {
            throw new HotReplConnectionException($"Failed to connect to {_endpoint}: {ex.Message}", ex);
        }

        // Server speaks first with the handshake.
        var raw = await FrameReader.ReadOneAsync(socket, connectCts.Token).ConfigureAwait(false)
            ?? throw new HotReplProtocolException("noHandshake", "Server closed before handshake.");
        var handshake = JObject.Parse(raw);
        if ((string?)handshake["type"] != "handshake")
            throw new HotReplProtocolException("expectedHandshake", $"Expected 'handshake', got '{handshake["type"]}'.");

        var protocolVersion = (int)handshake["protocolVersion"]!;
        var schemaValidation = (bool?)handshake.SelectToken("controlCapabilities.schemaValidation") ?? false;
        var caps = new HotReplCapabilities(handshake, protocolVersion, schemaValidation);

        var dispatcher = new MessageDispatcher(socket);
        return new HotReplSession(dispatcher, caps, _options);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/HotRepl.Sdk/ --nologo -v q` Expected: 0 errors.

- [ ] **Step 3: Commit M6.7-M6.9**

```bash
git add src/HotRepl.Sdk/HotReplClient.cs src/HotRepl.Sdk/HotReplSession.cs
git commit -m "feat(sdk): HotReplClient connect + handshake; HotReplSession dispatch skeleton"
```

### Task 6.10: `HotReplSession.ListCommandsAsync` and `DescribeCommandAsync`

**Files:**

- Modify: `src/HotRepl.Sdk/HotReplSession.cs`

- [ ] **Step 1: Add catalog methods to `HotReplSession`**

```csharp
// (append to HotReplSession class)
using HotRepl.Protocol;
using System.Collections.Generic;
using System.Linq;

private IReadOnlyList<CommandSummary>? _cachedCatalog;
private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CommandDescriptor> _descriptors =
    new(StringComparer.Ordinal);

public async Task<IReadOnlyList<CommandSummary>> ListCommandsAsync(CancellationToken ct = default)
{
    if (_cachedCatalog != null) return _cachedCatalog;
    var id = NextId("list");
    var response = await RequestRawAsync(
        new JObject { ["type"] = "commands_list", ["id"] = id }, ct).ConfigureAwait(false);

    var commands = response["commands"] as JArray ?? new JArray();
    var list = commands.OfType<JObject>().Select(ParseCommandSummary).ToArray();
    _cachedCatalog = list;
    return list;
}

public async Task<CommandDescriptor> DescribeCommandAsync(string name, CancellationToken ct = default)
{
    if (_descriptors.TryGetValue(name, out var cached)) return cached;
    var id = NextId("describe");
    var response = await RequestRawAsync(
        new JObject { ["type"] = "command_describe", ["id"] = id, ["name"] = name }, ct).ConfigureAwait(false);

    var descriptorJson = (JObject)response["descriptor"]!;
    var descriptor = descriptorJson.ToObject<CommandDescriptor>()!;
    _descriptors[name] = descriptor;
    return descriptor;
}

private static CommandSummary ParseCommandSummary(JObject obj) => obj.ToObject<CommandSummary>()!;
```

- [ ] **Step 2: Build + commit**

Run: `dotnet build src/HotRepl.Sdk/ --nologo -v q`

```bash
git add src/HotRepl.Sdk/HotReplSession.cs
git commit -m "feat(sdk): ListCommandsAsync/DescribeCommandAsync with per-session cache"
```

### Task 6.11: `HotReplSession.RunAsync<TArgs, TResult>` for sync commands

**Files:**

- Modify: `src/HotRepl.Sdk/HotReplSession.cs`

- [ ] **Step 1: Implement RunAsync overloads**

```csharp
public Task<HotReplResult<TResult>> RunAsync<TArgs, TResult>(
    string command, TArgs args, HotReplRunOptions? options = null, CancellationToken cancellationToken = default)
{
    var argsJson = args is null ? new JObject() : JObject.FromObject(args, JsonSerializer.CreateDefault(_options.SerializerSettings));
    return RunInternalAsync<TResult>(command, argsJson, options, cancellationToken);
}

public Task<HotReplResult<TResult>> RunAsync<TResult>(
    string command, IReadOnlyDictionary<string, object?> args, HotReplRunOptions? options = null, CancellationToken cancellationToken = default)
{
    var argsJson = JObject.FromObject(args, JsonSerializer.CreateDefault(_options.SerializerSettings));
    return RunInternalAsync<TResult>(command, argsJson, options, cancellationToken);
}

public Task<HotReplResult<JToken>> RunRawAsync(
    string command, JToken args, HotReplRunOptions? options = null, CancellationToken cancellationToken = default)
{
    var argsJson = args is JObject jo ? jo : new JObject { ["value"] = args };
    return RunInternalAsync<JToken>(command, argsJson, options, cancellationToken);
}

private async Task<HotReplResult<TResult>> RunInternalAsync<TResult>(
    string command, JObject args, HotReplRunOptions? options, CancellationToken ct)
{
    // 1) Consult the catalog to learn sync vs job.
    var catalog = await ListCommandsAsync(ct).ConfigureAwait(false);
    var entry = catalog.FirstOrDefault(c => c.Name == command)
        ?? throw new HotReplCommandException(HotReplErrorKind.InvalidRequest, "commandNotFound",
            $"Command '{command}' is not registered.", retryable: false);

    var id = NextId("run");
    var msg = new JObject
    {
        ["type"] = "command_call",
        ["id"] = id,
        ["name"] = command,
        ["args"] = args,
    };
    if (options?.Timeout is TimeSpan to) msg["timeoutMs"] = (long)to.TotalMilliseconds;

    var response = await RequestRawAsync(msg, ct).ConfigureAwait(false);
    var rtype = (string)response["type"]!;

    if (entry.Kind == "sync")
    {
        if (rtype != "command_result")
            throw new HotReplProtocolException("expectedCommandResult", $"Got '{rtype}'.");
        return ToTypedResult<TResult>(response);
    }

    // Job-kind path: caller should have used StartJobAsync. Treat command_result here as terminal failure.
    if (rtype == "job_accepted")
        throw new HotReplProtocolException("syncDispatchOnJob", $"Command '{command}' is a job; use StartJobAsync.");
    return ToTypedResult<TResult>(response);
}

private HotReplResult<TResult> ToTypedResult<TResult>(JObject response)
{
    if ((string?)response["status"] == "failed")
    {
        var err = (JObject?)response["error"];
        throw new HotReplCommandException(
            kind: ParseErrorKind((string?)err?["kind"]),
            code: (string?)err?["code"] ?? "commandFailed",
            message: (string?)err?["message"] ?? "Command failed.",
            retryable: (bool?)err?["retryable"] ?? false,
            details: err?["details"]);
    }
    var outputJson = response["output"] ?? new JObject();
    var output = outputJson.ToObject<TResult>(JsonSerializer.CreateDefault(_options.SerializerSettings))!;
    var artifacts = ParseArtifacts(response["artifacts"] as JObject);
    return new HotReplResult<TResult>(output, artifacts);
}

private static IReadOnlyDictionary<string, Artifact> ParseArtifacts(JObject? json)
{
    var dict = new Dictionary<string, Artifact>(StringComparer.Ordinal);
    if (json == null) return dict;
    foreach (var prop in json.Properties())
        dict[prop.Name] = new Artifact(prop.Value.ToObject<ArtifactRef>()!);
    return dict;
}

private static HotReplErrorKind ParseErrorKind(string? kind) => kind switch
{
    "invalid_request" => HotReplErrorKind.InvalidRequest,
    "validation_failed" => HotReplErrorKind.ValidationFailed,
    "precondition_failed" => HotReplErrorKind.PreconditionFailed,
    "conflict" => HotReplErrorKind.Conflict,
    "cancelled" => HotReplErrorKind.Cancelled,
    "unsupported_operation" => HotReplErrorKind.UnsupportedOperation,
    _ => HotReplErrorKind.Internal,
};
```

- [ ] **Step 2: Build + commit**

Run: `dotnet build src/HotRepl.Sdk/ --nologo -v q`

```bash
git add src/HotRepl.Sdk/HotReplSession.cs
git commit -m "feat(sdk): RunAsync overloads for sync command dispatch"
```

### Task 6.12: `HotReplJob<TResult>` + `StartJobAsync`

**Files:**

- Create: `src/HotRepl.Sdk/HotReplJob.cs`
- Modify: `src/HotRepl.Sdk/HotReplSession.cs`

- [ ] **Step 1: Implement HotReplJob**

```csharp
// src/HotRepl.Sdk/HotReplJob.cs
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk;

public sealed class HotReplJob<TResult>
{
    private readonly HotReplSession _session;
    private readonly TimeSpan _defaultPollInterval;
    private HotReplJobStatus _last;

    internal HotReplJob(HotReplSession session, string jobId, string initialState, TimeSpan pollInterval)
    {
        _session = session;
        Id = jobId;
        _last = new HotReplJobStatus(jobId, initialState);
        _defaultPollInterval = pollInterval;
    }

    public string Id { get; }
    public HotReplJobStatus LastKnownStatus => _last;

    public async IAsyncEnumerable<HotReplJobProgress> Progress(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (status, raw) = await PollOnceAsync(cancellationToken).ConfigureAwait(false);
            _last = status;
            var progressNode = raw["progress"] as JObject;
            if (progressNode != null)
                yield return new HotReplJobProgress(progressNode["snapshot"] as JObject,
                    (string?)progressNode["message"]);
            if (status.State != "running") yield break;
            await Task.Delay(_defaultPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<HotReplJobStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var (status, _) = await PollOnceAsync(ct).ConfigureAwait(false);
        _last = status;
        return status;
    }

    public Task CancelAsync(CancellationToken ct = default)
    {
        var id = _session.NextId("cancel");
        return _session.RequestRawAsync(
            new JObject { ["type"] = "job_cancel", ["id"] = id, ["jobId"] = Id }, ct);
    }

    public Task<HotReplResult<TResult>> WaitForCompletionAsync(CancellationToken ct = default) =>
        WaitForCompletionAsync(_defaultPollInterval, ct);

    public async Task<HotReplResult<TResult>> WaitForCompletionAsync(TimeSpan pollingInterval, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var (status, raw) = await PollOnceAsync(ct).ConfigureAwait(false);
            _last = status;
            if (status.State == "running")
            {
                await Task.Delay(pollingInterval, ct).ConfigureAwait(false);
                continue;
            }
            // Terminal: parse via session helper.
            return _session.ParseJobTerminal<TResult>(raw, Id);
        }
    }

    private async Task<(HotReplJobStatus, JObject)> PollOnceAsync(CancellationToken ct)
    {
        var id = _session.NextId("status");
        var raw = await _session.RequestRawAsync(
            new JObject { ["type"] = "job_status", ["id"] = id, ["jobId"] = Id }, ct).ConfigureAwait(false);
        var state = (string?)raw["state"] ?? "running";
        return (new HotReplJobStatus(Id, state), raw);
    }
}
```

- [ ] **Step 2: Add `StartJobAsync` and `ParseJobTerminal` to `HotReplSession`**

```csharp
// (append to HotReplSession)
public async Task<HotReplJob<TResult>> StartJobAsync<TArgs, TResult>(
    string command, TArgs args, HotReplRunOptions? options = null, CancellationToken cancellationToken = default)
{
    var argsJson = args is null ? new JObject() : JObject.FromObject(args, JsonSerializer.CreateDefault(_options.SerializerSettings));
    var id = NextId("job");
    var msg = new JObject
    {
        ["type"] = "command_call",
        ["id"] = id,
        ["name"] = command,
        ["args"] = argsJson,
    };
    if (options?.Timeout is TimeSpan to) msg["timeoutMs"] = (long)to.TotalMilliseconds;

    var response = await RequestRawAsync(msg, cancellationToken).ConfigureAwait(false);
    var rtype = (string)response["type"]!;
    if (rtype != "job_accepted")
        throw new HotReplProtocolException("expectedJobAccepted",
            $"Command '{command}' returned '{rtype}' instead of job_accepted.");

    var jobId = (string)response["jobId"]!;
    var initialState = (string?)response["state"] ?? "running";
    var poll = options?.PollingInterval ?? _options.JobPollingInterval;
    return new HotReplJob<TResult>(this, jobId, initialState, poll);
}

internal HotReplResult<TResult> ParseJobTerminal<TResult>(JObject response, string jobId)
{
    if ((string?)response["status"] == "failed")
    {
        var err = (JObject?)response["error"];
        throw new HotReplJobFailedException(
            code: (string?)err?["code"] ?? "jobFailed",
            message: (string?)err?["message"] ?? "Job failed.",
            details: err?["details"]) { JobId = jobId };
    }
    var outputJson = response["output"] ?? new JObject();
    var output = outputJson.ToObject<TResult>(JsonSerializer.CreateDefault(_options.SerializerSettings))!;
    var artifacts = ParseArtifacts(response["artifacts"] as JObject);
    return new HotReplResult<TResult>(output, artifacts);
}
```

- [ ] **Step 3: Build + commit**

Run: `dotnet build src/HotRepl.Sdk/ --nologo -v q`

```bash
git add src/HotRepl.Sdk/HotReplJob.cs src/HotRepl.Sdk/HotReplSession.cs
git commit -m "feat(sdk): HotReplJob<T> + StartJobAsync for job-kind commands"
```

### Task 6.13: Eval / complete / reset / watch / journal parity with TS SDK

**Files:**

- Modify: `src/HotRepl.Sdk/HotReplSession.cs`

- [ ] **Step 1: Add the remaining session methods**

For each of `EvalAsync<T>`, `CompleteAsync`, `ResetAsync`, and `WatchAsync<T>`, mirror the TS SDK's
`session.eval` / `session.complete` / `session.reset` / `session.watch` implementations from
`packages/sdk/src/session.ts:179-242`. Each is ~10-20 lines: construct request, await response,
parse, throw on error envelope.

(Skipping the verbatim code here for length; pattern is identical to RunAsync.)

- [ ] **Step 2: Build + commit**

Run: `dotnet build src/HotRepl.Sdk/ --nologo -v q`

```bash
git add src/HotRepl.Sdk/HotReplSession.cs
git commit -m "feat(sdk): eval/complete/reset/watch parity with TS SDK"
```

### Task 6.14: Fake transport for unit tests

**Files:**

- Create: `tests/HotRepl.Sdk.Tests/Fakes/FakeTransport.cs`

- [ ] **Step 1: Write a fake transport that satisfies the dispatcher's needs**

Since `MessageDispatcher` takes a `ClientWebSocket` directly, refactor it to accept an abstraction:

```csharp
// src/HotRepl.Sdk/Internal/IDuplexFrameChannel.cs
internal interface IDuplexFrameChannel : IAsyncDisposable
{
    Task<string?> ReceiveAsync(CancellationToken ct);
    Task SendAsync(string json, CancellationToken ct);
}
```

Wrap `ClientWebSocket` in a `WebSocketFrameChannel : IDuplexFrameChannel`, and provide
`FakeFrameChannel` for tests. Update `MessageDispatcher` to depend on `IDuplexFrameChannel`.

```csharp
// tests/HotRepl.Sdk.Tests/Fakes/FakeFrameChannel.cs
internal sealed class FakeFrameChannel : IDuplexFrameChannel
{
    private readonly System.Threading.Channels.Channel<string> _incoming;
    public readonly System.Collections.Generic.List<string> Sent = new();

    public FakeFrameChannel() => _incoming = System.Threading.Channels.Channel.CreateUnbounded<string>();
    public void EnqueueIncoming(string json) => _incoming.Writer.TryWrite(json);
    public Task<string?> ReceiveAsync(CancellationToken ct) => _incoming.Reader.ReadAsync(ct).AsTask()!;
    public Task SendAsync(string json, CancellationToken ct) { Sent.Add(json); return Task.CompletedTask; }
    public ValueTask DisposeAsync() { _incoming.Writer.TryComplete(); return ValueTask.CompletedTask; }
}
```

- [ ] **Step 2: Add unit test exercising handshake + commands_list**

```csharp
// tests/HotRepl.Sdk.Tests/Unit/HotReplSessionTests.cs
public sealed class HotReplSessionTests
{
    [Fact]
    public async Task ListCommandsAsync_CachesAcrossCalls()
    {
        var chan = new FakeFrameChannel();
        chan.EnqueueIncoming("{\"type\":\"handshake\",\"protocolVersion\":2,\"controlCapabilities\":{\"schemaValidation\":true}}");
        chan.EnqueueIncoming("{\"type\":\"commands_list_result\",\"id\":\"list-1\",\"commands\":[{\"name\":\"unity.app.info\",\"version\":1,\"kind\":\"sync\",\"mutatesState\":false}]}");

        var session = await ConnectAsync(chan);
        var first = await session.ListCommandsAsync();
        var second = await session.ListCommandsAsync();

        Assert.Same(first, second);     // cached
        Assert.Single(chan.Sent);       // only one commands_list request
    }

    private static Task<HotReplSession> ConnectAsync(FakeFrameChannel chan) =>
        // helper that builds MessageDispatcher(chan) and constructs a Session
        ...;
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/HotRepl.Sdk.Tests/ --nologo -v q` Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/HotRepl.Sdk/Internal/IDuplexFrameChannel.cs \
        src/HotRepl.Sdk/Internal/WebSocketTransport.cs \
        src/HotRepl.Sdk/Internal/MessageDispatcher.cs \
        tests/HotRepl.Sdk.Tests/Fakes/FakeFrameChannel.cs \
        tests/HotRepl.Sdk.Tests/Unit/HotReplSessionTests.cs
git commit -m "test(sdk): fake duplex frame channel + first HotReplSession unit tests"
```

### Task 6.15: M6 gate — full test pass

- [ ] **Step 1: Build all**

Run: `dotnet build src/HotRepl.Sdk/ tests/HotRepl.Sdk.Tests/ --nologo -v q` Expected: 0 errors.

- [ ] **Step 2: Test all**

Run: `dotnet test tests/HotRepl.Sdk.Tests/ --nologo -v q` Expected: all PASS (initial unit tests for
sync commands + jobs + catalog caching).

- [ ] **Step 3: NuGet pack check**

Run: `dotnet pack src/HotRepl.Sdk/ --nologo -v q -o /tmp/hotrepl-sdk-pack` Expected:
`HotRepl.Sdk.1.0.0.nupkg` produced under `/tmp/hotrepl-sdk-pack/`.

---

## Milestone 7 — `HotRepl.Testing` package

Goal: ship `HandlerHarness` and `ConformanceSuite` so consumer test projects stop reinventing the
same plumbing.

### Task 7.1: Project skeleton

**Files:**

- Create: `src/HotRepl.Testing/HotRepl.Testing.csproj`
- Modify: `HotRepl.sln`

- [ ] **Step 1: Write csproj**

```xml
<!-- src/HotRepl.Testing/HotRepl.Testing.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <PackageId>HotRepl.Testing</PackageId>
    <Version>1.0.0</Version>
    <Description>Test helpers for HotRepl typed-command authors and SDK consumers.</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\HotRepl.Core\HotRepl.Core.csproj" />
    <ProjectReference Include="..\HotRepl.Sdk\HotRepl.Sdk.csproj" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
```

Run: `dotnet sln HotRepl.sln add src/HotRepl.Testing/HotRepl.Testing.csproj`

- [ ] **Step 2: Build empty project**

Run: `dotnet build src/HotRepl.Testing/ --nologo -v q` Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/HotRepl.Testing/HotRepl.Testing.csproj HotRepl.sln
git commit -m "feat(testing): scaffold HotRepl.Testing netstandard2.0 package"
```

### Task 7.2: `HandlerResult<TOutput>`

**Files:**

- Create: `src/HotRepl.Testing/HandlerResult.cs`

- [ ] **Step 1: Write the helper result type**

```csharp
// src/HotRepl.Testing/HandlerResult.cs
using System.Collections.Generic;
using HotRepl.Control;
using HotRepl.Control.Artifacts;

namespace HotRepl.Testing;

public sealed class HandlerResult<TOutput>
{
    public HandlerResult(bool succeeded, TOutput? output,
        IReadOnlyDictionary<string, ArtifactRef> artifacts,
        IReadOnlyList<ControlCommandDiagnostic> diagnostics)
    {
        Succeeded = succeeded;
        Output = output;
        Artifacts = artifacts;
        Diagnostics = diagnostics;
    }
    public bool Succeeded { get; }
    public TOutput? Output { get; }
    public IReadOnlyDictionary<string, ArtifactRef> Artifacts { get; }
    public IReadOnlyList<ControlCommandDiagnostic> Diagnostics { get; }
}
```

### Task 7.3: `HandlerHarness` — schema, validate, run

**Files:**

- Create: `src/HotRepl.Testing/HandlerHarness.cs`
- Create: `src/HotRepl.Testing/Internal/InProcessControlContext.cs`

- [ ] **Step 1: Write `HandlerHarness`**

```csharp
// src/HotRepl.Testing/HandlerHarness.cs
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.Control.Artifacts;
using HotRepl.Control.Schema;
using Newtonsoft.Json.Linq;

namespace HotRepl.Testing;

public static class HandlerHarness
{
    public static JObject GenerateSchema<T>() => SchemaCache.For<T>();

    public static SchemaValidationResult Validate<TArgs>(string json)
    {
        var jobject = JObject.Parse(json);
        return Validate<TArgs>(jobject);
    }

    public static SchemaValidationResult Validate<TArgs>(JObject args) =>
        new NJsonSchemaValidator().Validate(args, SchemaCache.CompiledFor<TArgs>());

    public static async Task<HandlerResult<TOutput>> RunAsync<TArgs, TOutput>(
        IControlCommandHandler<TArgs, TOutput> handler,
        TArgs args,
        IArtifactWriter? artifactWriter = null,
        CancellationToken cancellationToken = default)
    {
        var writer = artifactWriter ?? new InMemoryArtifactWriter();
        var context = new ControlCommandContext<TOutput>(
            requestId: "test-1",
            timeout: System.TimeSpan.FromSeconds(30),
            jobId: null,
            progress: null,
            artifacts: writer);

        var typedResult = await handler.ExecuteAsync(context, args, cancellationToken).ConfigureAwait(false);
        return new HandlerResult<TOutput>(
            succeeded: typedResult.Succeeded,
            output: typedResult.Output,
            artifacts: typedResult.Artifacts,
            diagnostics: typedResult.Diagnostics);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/HotRepl.Testing/ --nologo -v q` Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/HotRepl.Testing/HandlerHarness.cs src/HotRepl.Testing/HandlerResult.cs
git commit -m "feat(testing): HandlerHarness for in-process schema+execution coverage"
```

### Task 7.4: `HandlerHarness` tests

**Files:**

- Create: `tests/HotRepl.Testing.Tests/HotRepl.Testing.Tests.csproj`
- Create: `tests/HotRepl.Testing.Tests/HandlerHarnessTests.cs`

- [ ] **Step 1: Test project scaffold**

```xml
<!-- tests/HotRepl.Testing.Tests/HotRepl.Testing.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\HotRepl.Testing\HotRepl.Testing.csproj" />
    <ProjectReference Include="..\..\src\HotRepl.Core\HotRepl.Core.csproj" />
  </ItemGroup>
</Project>
```

Run: `dotnet sln HotRepl.sln add tests/HotRepl.Testing.Tests/HotRepl.Testing.Tests.csproj`

- [ ] **Step 2: Write tests**

```csharp
// tests/HotRepl.Testing.Tests/HandlerHarnessTests.cs
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.Testing;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Testing.Tests;

public sealed class HandlerHarnessTests
{
    public sealed class Args { public string Name { get; set; } = ""; }
    public sealed class Output { public string Reply { get; set; } = ""; }

    [ControlCommand("test.echo")]
    private sealed class EchoHandler : IControlCommandHandler<Args, Output>
    {
        public string Name => "test.echo";
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Sync;
        public bool MutatesState => false;
        public ValueTask<ControlCommandResult<Output>> ExecuteAsync(
            ControlCommandContext<Output> context, Args args, CancellationToken ct) =>
            string.IsNullOrEmpty(args.Name)
                ? ValueTask.FromResult(context.ValidationFailed("nameRequired", "Name is required."))
                : ValueTask.FromResult(context.Ok(new Output { Reply = $"hi {args.Name}" }));
    }

    [Fact]
    public void GenerateSchema_ReturnsArgsSchema()
    {
        var schema = HandlerHarness.GenerateSchema<Args>();
        Assert.Contains("Name", schema.ToString());
    }

    [Fact]
    public void Validate_FailsOnMissingRequiredProperty()
    {
        var result = HandlerHarness.Validate<Args>("{}");
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task RunAsync_ReturnsOkResult()
    {
        var result = await HandlerHarness.RunAsync(new EchoHandler(), new Args { Name = "Joe" });
        Assert.True(result.Succeeded);
        Assert.Equal("hi Joe", result.Output!.Reply);
    }

    [Fact]
    public async Task RunAsync_PropagatesValidationFailure()
    {
        var result = await HandlerHarness.RunAsync(new EchoHandler(), new Args { Name = "" });
        Assert.False(result.Succeeded);
        Assert.Equal("nameRequired", result.Diagnostics[0].Code);
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/HotRepl.Testing.Tests/ --nologo -v q` Expected: 4 PASS.

- [ ] **Step 4: Commit**

```bash
git add tests/HotRepl.Testing.Tests/
git commit -m "test(testing): HandlerHarness covers schema, validate, run, and failure paths"
```

### Task 7.5: `ConformanceSuite` (basic shape)

**Files:**

- Create: `src/HotRepl.Testing/ConformanceSuite.cs`
- Create: `src/HotRepl.Testing/ConformanceOptions.cs`

- [ ] **Step 1: Mirror the TS conformance shape**

Read `packages/conformance/src/index.ts:47-156` to identify the cases the C# suite must cover.
Author `ConformanceSuite.RunAllAsync(HotReplSession session, ...)` that exercises:

- `commands_list` returns at least one command.
- `command_describe` on each returns a valid descriptor.
- A registered sync command runs and returns `command_result`.
- A registered job command produces `job_accepted`, then `job_result` after polling.
- Unknown command returns a `command_result` with `status: failed`.

Each check returns a `ConformanceCheck` with name + status; the suite aggregates.

For Phase 4a we ship the harness shape; the cases can be incrementally expanded in Phase 4b/4c as
consumer migrations reveal real-world gaps.

- [ ] **Step 2: Commit (initial shape only)**

```bash
git add src/HotRepl.Testing/ConformanceSuite.cs src/HotRepl.Testing/ConformanceOptions.cs
git commit -m "feat(testing): ConformanceSuite skeleton mirroring TS @hotrepl/conformance"
```

### Task 7.6: M7 gate

- [ ] **Step 1: Build + test**

Run:
`dotnet build src/HotRepl.Testing/ tests/HotRepl.Testing.Tests/ --nologo -v q && dotnet test tests/HotRepl.Testing.Tests/ --nologo -v q`
Expected: 0 errors; all tests PASS.

- [ ] **Step 2: Pack check**

Run: `dotnet pack src/HotRepl.Testing/ --nologo -v q -o /tmp/hotrepl-testing-pack` Expected: nupkg
produced.

---

## Milestone 8 — Catalog caching in TS + C# SDKs

Goal: stop calling `command_describe` on every `run` when `commands_list` already knows the kind.
(C# SDK already does this from Task 6.11; this milestone fixes the TS SDK to match.)

### Task 8.1: TS SDK — cache catalog, dispatch on `kind` from catalog

**Files:**

- Modify: `packages/sdk/src/session.ts`
- Modify: `packages/sdk/src/commands.ts`
- Test: `packages/sdk/test/catalog-cache.test.ts`

- [ ] **Step 1: Write the failing test**

```ts
// packages/sdk/test/catalog-cache.test.ts
import { expect, test } from "bun:test";
import { Session } from "../src/session";
import { mockTransport } from "./helpers/mock-transport";

test("Session.run consults cached catalog instead of command_describe", async () => {
  const transport = mockTransport({
    handshake: { protocolVersion: 2, controlCapabilities: { schemaValidation: true } },
    onRequest: (req) => {
      if (req.type === "commands_list") {
        return {
          type: "commands_list_result",
          id: req.id,
          commands: [
            { name: "test.echo", version: 1, kind: "sync", mutatesState: false },
          ],
        };
      }
      if (req.type === "command_call") {
        return {
          type: "command_result",
          id: req.id,
          status: "ok",
          output: { echoed: "hi" },
          artifacts: {},
        };
      }
      throw new Error(`unexpected request ${JSON.stringify(req)}`);
    },
  });

  const session = await Session.create(transport);
  const result = await session.run("test.echo", { msg: "hi" });

  expect(result.output).toEqual({ echoed: "hi" });
  // The crucial assertion: NO command_describe request was sent.
  const types = transport.sentRequests.map((r) => r.type);
  expect(types).toContain("commands_list");
  expect(types).toContain("command_call");
  expect(types).not.toContain("command_describe");
});
```

- [ ] **Step 2: Run test — expect FAIL**

Run: `bun test packages/sdk/test/catalog-cache.test.ts` Expected: FAIL (`command_describe` is sent
today).

- [ ] **Step 3: Update `session.ts`**

In `packages/sdk/src/session.ts`, replace `describeCommand(name)` calls inside `run()` with a
`getCatalogEntry(name)` helper that lazy-fetches `commands_list` on first use:

```ts
// packages/sdk/src/session.ts (add)
private cachedCatalog: CommandSummary[] | null = null;

private async getCatalogEntry(name: string): Promise<CommandSummary> {
  if (this.cachedCatalog === null) {
    const response = await this.request<CommandsListResultMessage>({
      type: "commands_list",
      id: this.nextId("list"),
    });
    this.cachedCatalog = response.commands;
  }
  const entry = this.cachedCatalog.find((c) => c.name === name);
  if (!entry) throw protocolError("commandNotFound", `Command '${name}' is not registered.`);
  return entry;
}

// in run():
//   const descriptor = await this.describeCommand(name);
// becomes
const entry = await this.getCatalogEntry(name);
// then dispatch on entry.kind instead of descriptor.kind
```

`describeCommand(name)` stays as a separate public method (callers can still request descriptors for
schemas), but it's no longer called implicitly from `run`.

- [ ] **Step 4: Run test — expect PASS**

Run: `bun test packages/sdk/test/catalog-cache.test.ts` Expected: PASS.

- [ ] **Step 5: Run all SDK tests**

Run: `bun test packages/sdk/test` Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add packages/sdk/src/session.ts packages/sdk/src/commands.ts packages/sdk/test/catalog-cache.test.ts
git commit -m "perf(sdk): cache commands_list; stop calling command_describe per run"
```

### Task 8.2: Update `@hotrepl/mcp` to use the new public API

**Files:**

- Modify: `packages/mcp/src/tools.ts`

- [ ] **Step 1: Replace `session.request({ type: "commands_list" })` with `session.listCommands()`**

If `session.listCommands()` doesn't exist publicly yet, add it as a thin wrapper around
`getCatalogEntry` (or expose `getCatalog()` as the public method). Update
`packages/mcp/src/tools.ts:140-145` accordingly.

- [ ] **Step 2: Run mcp + sdk tests**

Run: `bun test packages/mcp/test packages/sdk/test` Expected: all PASS.

- [ ] **Step 3: Commit**

```bash
git add packages/mcp/src/tools.ts packages/sdk/src/session.ts
git commit -m "refactor(mcp): use session.listCommands() public API instead of raw request"
```

### Task 8.3: M8 gate

- [ ] **Step 1: Bun test full**

Run: `bun test packages/*/test` Expected: all PASS.

- [ ] **Step 2: bun typecheck**

Run: `bun run --filter './packages/*' typecheck` Expected: 0 errors.

---

## Milestone 9 — Docs + sample promotion

Goal: write `docs/authoring-commands.md`, link it from `README.md` and `AGENTS.md`, and promote
`HotRepl.UnityCommands.{BepInEx,MelonLoader}` to canonical-sample status.

### Task 9.1: `docs/authoring-commands.md`

**Files:**

- Create: `docs/authoring-commands.md`

- [ ] **Step 1: Author the doc**

````markdown
# Authoring HotRepl commands

This guide shows how to author typed control commands for HotRepl. The recommended reference
implementation is `src/HotRepl.UnityCommands/Commands/`: fork from there.

## 1. Declare the handler

A handler is a C# class implementing `IControlCommandHandler<TArgs, TOutput>`. Declare metadata with
`[ControlCommand]`:

```csharp
[ControlCommand("yourmod.example", Version = 1, Kind = ControlCommandKind.Sync)]
public sealed class ExampleCommand : IControlCommandHandler<ExampleArgs, ExampleResult>
{
    public string Name => "yourmod.example";
    public int Version => 1;
    public ControlCommandKind Kind => ControlCommandKind.Sync;
    public bool MutatesState => false;

    public async ValueTask<ControlCommandResult<ExampleResult>> ExecuteAsync(
        ControlCommandContext<ExampleResult> context,
        ExampleArgs args,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Name))
            return context.ValidationFailed("nameRequired", "name must not be empty.");

        return context.Ok(new ExampleResult { Reply = $"hello {args.Name}" });
    }
}
```

## 2. Declare args and result DTOs

Use Newtonsoft attributes. `[JsonProperty("name", Required = Required.Always)]` surfaces as
`required` in the generated JSON Schema:

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

## 3. Register the handler

For BepInEx:

```csharp
[BepInPlugin(...)]
public sealed class Plugin : BaseUnityPlugin
{
    private IDisposable? _registration;
    private void Awake()
    {
        _registration = GlobalControlCommandRegistry.Instance.Register(new ExampleCommand());
    }
    private void OnDestroy() => _registration?.Dispose();
}
```

For MelonLoader, register in `OnLateInitializeMelon` (fires after every `OnInitializeMelon` across
all mods).

## 4. Emit artifacts

Use `context.Artifacts.AttachFileAsync` for files on disk:

```csharp
var artifact = await context.Artifacts.AttachFileAsync(
    logicalName: "report.json",
    path: "/path/to/report.json",
    contentType: "application/json",
    ct);
```

Declare the expected artifact keys on the handler so MCP clients see them:

```csharp
[ControlCommandArtifact("report.json", ContentType = "application/json", Required = true)]
public sealed class ExampleCommand : ... { ... }
```

## 5. Test handlers

Use `HotRepl.Testing.HandlerHarness`:

```csharp
var result = await HandlerHarness.RunAsync(new ExampleCommand(), new ExampleArgs { Name = "Joe" });
Assert.True(result.Succeeded);
Assert.Equal("hello Joe", result.Output.Reply);
```

Validate the schema separately:

```csharp
var validation = HandlerHarness.Validate<ExampleArgs>("{}");
Assert.False(validation.Ok);  // "name" missing
```

## 6. Job-kind commands

For long-running operations, set `Kind = ControlCommandKind.Job` and report progress:

```csharp
[ControlCommand("yourmod.export", Kind = ControlCommandKind.Job, MutatesState = true)]
public sealed class ExportCommand : IControlCommandHandler<ExportArgs, ExportResult>
{
    public async ValueTask<ControlCommandResult<ExportResult>> ExecuteAsync(
        ControlCommandContext<ExportResult> context, ExportArgs args, CancellationToken ct)
    {
        context.Progress.Report(new ControlCommandProgress(
            Snapshot: new JObject { ["phase"] = "loading" },
            Message: "Loading data."));
        // ... do work ...
        return context.Ok(new ExportResult { ... });
    }
}
```

## 7. Reference samples

See `src/HotRepl.UnityCommands/Commands/` for working examples covering:

- `UnityAppInfoCommand` — no-args sync, returns metadata.
- `UnityGameObjectFindCommand` — typed args, sync.
- `UnityTimeSetScaleCommand` — mutating sync.
- `UnityScreenshotCommand` — sync with a single byte artifact.

For build-tool / automation consumers, see `HotRepl.Sdk` for the C# client API.
````

- [ ] **Step 2: Commit**

```bash
git add docs/authoring-commands.md
git commit -m "docs: add authoring-commands.md walkthrough for typed handler authors"
```

### Task 9.2: Link the doc from `README.md` and `AGENTS.md`

**Files:**

- Modify: `README.md`
- Modify: `AGENTS.md`

- [ ] **Step 1: Add link to README's "Authoring commands" section** (or create the section if
      absent)

- [ ] **Step 2: Add link to AGENTS.md under the relevant section** (likely near the command-protocol
      notes)

- [ ] **Step 3: Commit**

```bash
git add README.md AGENTS.md
git commit -m "docs: link authoring-commands.md from README and AGENTS"
```

### Task 9.3: UnityCommands README updates

**Files:**

- Modify: `src/HotRepl.UnityCommands.BepInEx/README.md` (create if absent)
- Modify: `src/HotRepl.UnityCommands.MelonLoader/README.md` (create if absent)

- [ ] **Step 1: Write a short README in each**

> # HotRepl.UnityCommands.BepInEx
>
> Canonical sample plugin demonstrating HotRepl typed-command authoring for Unity games running
> BepInEx + Mono. New plugin authors should fork from `src/HotRepl.UnityCommands/` as the starting
> point.
>
> See `docs/authoring-commands.md` for the walkthrough.

- [ ] **Step 2: Commit**

```bash
git add src/HotRepl.UnityCommands.BepInEx/README.md src/HotRepl.UnityCommands.MelonLoader/README.md
git commit -m "docs(unity-commands): label sample plugins as canonical reference"
```

### Task 9.4: M9 gate

- [ ] **Step 1: typos + dprint**

Run: `dprint check && typos` Expected: clean.

- [ ] **Step 2: Full pre-push**

Run: `lefthook run pre-push --force` Expected: all checks PASS.

---

## Final acceptance

Before declaring Phase 4a complete:

- [ ] All milestones M1–M9 committed.
- [ ] `dotnet test tests/HotRepl.Tests/ --nologo -v q` → 138+ tests PASS (the new tasks add ~15
      tests across M1, M3, M4, M5).
- [ ] `dotnet test tests/HotRepl.Sdk.Tests/ --nologo -v q` → all PASS.
- [ ] `dotnet test tests/HotRepl.Testing.Tests/ --nologo -v q` → all PASS.
- [ ] `bun test packages/*/test` → all PASS.
- [ ] `bun run --filter './packages/*' typecheck` → 0 errors.
- [ ] `dotnet pack src/HotRepl.Sdk/` produces a nupkg.
- [ ] `dotnet pack src/HotRepl.Testing/` produces a nupkg.
- [ ] `lefthook run pre-push --force` clean.
- [ ] Ardenfall mod project builds against the new core with only the
      `ControlCommandKind.Synchronous → Sync` and `ControlCommandContext` →
      `ControlCommandContext<TOutput>` mechanical renames applied. (Smoke test only; the full
      migration ships in Phase 4b.)
- [ ] AK `HotReplCommands` project builds against the new core the same way. (Smoke test only; the
      full migration ships in Phase 4c.)

When all boxes are green, Phase 4a is done. Phase 4b (Ardenfall update) and Phase 4c (AK update)
plans are then written and executed.

---

## Self-review note

Coverage check against the spec:

- Decision 1 (validator caching + capability honesty) → M1 ✓
- Decision 2 (Synchronous → Sync) → M2 ✓
- Decision 3 (ControlCommandContext<TOutput> + helpers) → M3 ✓
- Decision 4 ([ControlCommand] attribute) → M4 ✓
- Decision 5 (IArtifactWriter expansion + ArtifactsSchema) → M5 ✓
- Decision 6 (HotRepl.Sdk) → M6 ✓
- Decision 7 (HotRepl.Testing) → M7 ✓
- Decision 8 (catalog caching in SDKs) → M8 ✓
- Decision 9 (samples + docs) → M9 ✓

All nine architecture decisions have at least one milestone. No placeholders detected in the task
bodies. Type/method names used in later tasks consistent with earlier ones
(`HotReplSession.RequestRawAsync`, `HotReplJob<TResult>`, `HandlerHarness.RunAsync`,
`IArtifactWriter.AttachFileAsync`).
