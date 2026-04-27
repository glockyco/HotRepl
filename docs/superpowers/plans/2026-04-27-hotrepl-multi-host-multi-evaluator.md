# HotRepl Multi-Host Multi-Evaluator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use skill://superpowers:subagent-driven-development (recommended) or skill://superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor HotRepl into a host-agnostic, evaluator-pluggable runtime C# REPL that preserves BepInEx/Mono behavior and adds Roslyn, MelonLoader, and IL2CPP support.

**Architecture:** `HotRepl.Core` owns protocol, engine, serialization, and public evaluator/host contracts only. Concrete compiler stacks move into evaluator assemblies, Unity/IL2CPP helpers move into helper assemblies, and BepInEx/MelonLoader projects become thin lifecycle hosts that provide evaluator factories and platform metadata. The Python client remains protocol-only and treats evaluator/host metadata as optional fields for backward compatibility.

**Tech Stack:** C#/.NET (`netstandard2.1`, `net6.0` for MelonLoader and isolated Roslyn), Mono.CSharp, Roslyn Scripting, Fleck, Newtonsoft.Json, xUnit, Python 3.11+, websockets, pytest, ruff, mypy.

---

## Scope and sequencing

This plan implements only the HotRepl prerequisite. It does not add Ancient Kingdoms visual-audit scripts, game-specific probes, or website features.

The approved spec contains several subsystems, but they are not independent for implementation: MelonLoader and the Ancient Kingdoms runtime audit depend on the Core contract, Roslyn evaluator, and helper extraction. The sequence below keeps the repo buildable after every commit and preserves BepInEx/Mono at each checkpoint.

One implementation detail intentionally tightens the spec: `ICodeEvaluator.Evaluate(string)` becomes `Evaluate(string, CancellationToken)`. The approved spec requires cooperative cancellation for Roslyn; passing the token through the evaluator boundary is the smallest honest way to implement that requirement. Mono.CSharp ignores the token and keeps `HardAbort` behavior.

## File structure after completion

Create:

- `src/HotRepl.Core/Evaluator/EvaluatorCapabilities.cs` — public evaluator capability metadata and timeout mode enum.
- `src/HotRepl.Core/HostInfo.cs` — public host metadata reported in the handshake.
- `src/HotRepl.Core/Evaluator/SelectEvaluatorCommand.cs` — command value for evaluator switching.
- `src/HotRepl.Evaluator.MonoCSharp/HotRepl.Evaluator.MonoCSharp.csproj` — Mono.CSharp evaluator assembly.
- `src/HotRepl.Evaluator.MonoCSharp/MonoCSharpEvaluator.cs` — moved evaluator implementation.
- `src/HotRepl.Evaluator.MonoCSharp/AssemblyFilter.cs` — moved ScriptEngine assembly filter.
- `src/HotRepl.Helpers.Unity/HotRepl.Helpers.Unity.csproj` — Unity helper assembly.
- `src/HotRepl.Helpers.Unity/UnityHelpers.cs` — moved Unity helper implementation.
- `src/HotRepl.Evaluator.Roslyn/HotRepl.Evaluator.Roslyn.csproj` — Roslyn evaluator assembly, multi-targeted for `netstandard2.1` and `net6.0`.
- `src/HotRepl.Evaluator.Roslyn/RoslynScriptEvaluator.cs` — persistent Roslyn script evaluator.
- `src/HotRepl.Evaluator.Roslyn/RoslynIsolatedEvaluator.cs` — `net6.0` isolated evaluator.
- `src/HotRepl.Evaluator.Roslyn/RoslynEvaluatorFactory.cs` — factory helpers exposed to hosts.
- `src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj` — MelonLoader host assembly.
- `src/HotRepl.Host.MelonLoader/MelonLoaderHost.cs` — `IReplHost` implementation for MelonLoader.
- `src/HotRepl.Host.MelonLoader/ReplMod.cs` — MelonLoader lifecycle entry point.
- `src/HotRepl.Helpers.Il2Cpp/HotRepl.Helpers.Il2Cpp.csproj` — IL2CPP helper assembly.
- `src/HotRepl.Helpers.Il2Cpp/Il2CppHelpers.cs` — runtime type lookup, safe names, casting helpers.
- `tests/HotRepl.Tests/Unit/EvaluatorCapabilitiesTests.cs` — capability and host metadata tests.
- `tests/HotRepl.Tests/Unit/RoslynScriptEvaluatorTests.cs` — Roslyn script evaluator tests.
- `tests/HotRepl.Tests/Unit/RoslynIsolatedEvaluatorTests.cs` — isolated evaluator tests compiled only on runtimes that support collectible load contexts.
- `tests/HotRepl.Tests/Unit/SelectEvaluatorProtocolTests.cs` — protocol tests for evaluator switching.
- `client/tests/test_select_evaluator.py` — client smoke tests for evaluator selection when a running server supports it.

Modify:

- `HotRepl.slnx` — add new projects.
- `src/HotRepl.Core/HotRepl.Core.csproj` — remove `mcs.dll`; keep only Core dependencies.
- `src/HotRepl.Core/IReplHost.cs` — add host metadata and evaluator factory members.
- `src/HotRepl.Core/ReplConfig.cs` — add optional default evaluator override.
- `src/HotRepl.Core/Evaluator/ICodeEvaluator.cs` — make public, add capabilities, pass cancellation token.
- `src/HotRepl.Core/Evaluator/EvalOutcome.cs` — make public because external evaluators return it.
- `src/HotRepl.Core/Evaluator/CompletionResult.cs` — make public because external evaluators return it.
- `src/HotRepl.Core/Evaluator/EvalJob.cs` — carry a `CancellationTokenSource` for cooperative evaluators.
- `src/HotRepl.Core/Protocol/Messages.cs` — add metadata fields and select-evaluator messages.
- `src/HotRepl.Core/Server/MessageRouter.cs` — route `select_evaluator`.
- `src/HotRepl.Core/ReplEngine.cs` — construct evaluators through the host, honor timeout mode, report metadata, switch evaluators.
- `src/HotRepl.Core/Helpers/Repl.cs` — update comments to say Unity helpers live in `HotRepl.Helpers.Unity`.
- `src/HotRepl.Core/Helpers/HelperInjector.cs` — no platform-specific logic; keep generic assembly and namespace injection.
- `src/HotRepl.BepInEx/HotRepl.BepInEx.csproj` — reference Mono evaluator, Roslyn evaluator, and Unity helpers.
- `src/HotRepl.BepInEx/BepInExHost.cs` — expose host info and evaluator factories; default to Mono.CSharp unless config overrides.
- `src/HotRepl.BepInEx/ReplPlugin.cs` — initialize `HotRepl.Helpers.Unity.UnityHelpers`.
- `tests/HotRepl.Tests/HotRepl.Tests.csproj` — reference new evaluator projects.
- `client/src/hotrepl/_types.py` — type metadata and select messages.
- `client/src/hotrepl/_client.py` — add `select_evaluator` and clearer unsupported errors.
- `client/src/hotrepl/cli.py` — add `info` and `select-evaluator` commands.
- `client/tests/test_handshake.py` — assert metadata when present while preserving compatibility.
- `README.md`, `AGENTS.md`, `.claude/skills/hotrepl/SKILL.md` — document hosts, evaluators, timeout semantics, and validation.
- `.github/workflows/ci.yml` — build Core, Mono evaluator, Roslyn evaluator, and tests; keep Unity/MelonLoader projects out of CI unless references are available.

## Shared contracts to preserve

Use these exact names through the plan:

```csharp
namespace HotRepl.Evaluator;

public enum TimeoutMode
{
    HardAbort,
    Cooperative,
    None,
}

public sealed class EvaluatorCapabilities
{
    public string Name { get; init; } = string.Empty;
    public string LanguageVersion { get; init; } = string.Empty;
    public bool SupportsPersistentState { get; init; }
    public bool SupportsCompletion { get; init; }
    public TimeoutMode TimeoutMode { get; init; }
}
```

```csharp
namespace HotRepl;

public sealed class HostInfo
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Runtime { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
}
```

`IReplHost` owns evaluator construction:

```csharp
public interface IReplHost
{
    ReplConfig Config { get; }
    HostInfo HostInfo { get; }
    IReadOnlyList<EvaluatorCapabilities> AvailableEvaluators { get; }
    string DefaultEvaluatorName { get; }
    ICodeEvaluator CreateEvaluator(string evaluatorName);

    void LogInfo(string message);
    void LogDebug(string message);
    void LogWarning(string message);
    void LogError(string message, System.Exception? ex = null);

    IReadOnlyList<Assembly> AdditionalAssemblies { get; }
    IReadOnlyList<string> AdditionalUsings { get; }
    string[] AdditionalHelperSignatures { get; }
}
```

The active evaluator is selected by name. Unknown evaluator names must return protocol `select_evaluator_error` with `errorKind: "unsupported"`, not fall back silently.

---

### Task 1: Core evaluator capability contract and metadata handshake

**Files:**
- Create: `src/HotRepl.Core/Evaluator/EvaluatorCapabilities.cs`
- Create: `src/HotRepl.Core/HostInfo.cs`
- Create: `tests/HotRepl.Tests/Unit/EvaluatorCapabilitiesTests.cs`
- Modify: `src/HotRepl.Core/Evaluator/ICodeEvaluator.cs`
- Modify: `src/HotRepl.Core/Evaluator/EvalOutcome.cs`
- Modify: `src/HotRepl.Core/Evaluator/CompletionResult.cs`
- Modify: `src/HotRepl.Core/Evaluator/EvalJob.cs`
- Modify: `src/HotRepl.Core/IReplHost.cs`
- Modify: `src/HotRepl.Core/ReplConfig.cs`
- Modify: `src/HotRepl.Core/Evaluator/MonoCSharpEvaluator.cs`
- Modify: `src/HotRepl.BepInEx/BepInExHost.cs`
- Modify: `src/HotRepl.Core/Protocol/Messages.cs`
- Modify: `src/HotRepl.Core/ReplEngine.cs`
- Modify: `tests/HotRepl.Tests/Unit/MessageSerializerTests.cs`

- [ ] **Step 1: Write failing metadata serialization tests**

Add `tests/HotRepl.Tests/Unit/EvaluatorCapabilitiesTests.cs`:

```csharp
using HotRepl;
using HotRepl.Evaluator;
using Xunit;

namespace HotRepl.Tests.Unit;

public class EvaluatorCapabilitiesTests
{
    [Fact]
    public void TimeoutMode_NamesAreStableWireValues()
    {
        Assert.Equal("HardAbort", TimeoutMode.HardAbort.ToString());
        Assert.Equal("Cooperative", TimeoutMode.Cooperative.ToString());
        Assert.Equal("None", TimeoutMode.None.ToString());
    }

    [Fact]
    public void Capabilities_CarryEvaluatorContract()
    {
        var capabilities = new EvaluatorCapabilities
        {
            Name = "Mono.CSharp",
            LanguageVersion = "7.x",
            SupportsPersistentState = true,
            SupportsCompletion = true,
            TimeoutMode = TimeoutMode.HardAbort,
        };

        Assert.Equal("Mono.CSharp", capabilities.Name);
        Assert.Equal("7.x", capabilities.LanguageVersion);
        Assert.True(capabilities.SupportsPersistentState);
        Assert.True(capabilities.SupportsCompletion);
        Assert.Equal(TimeoutMode.HardAbort, capabilities.TimeoutMode);
    }

    [Fact]
    public void HostInfo_CarriesHostMetadata()
    {
        var host = new HostInfo
        {
            Name = "BepInEx",
            Version = "5.x",
            Runtime = ".NET Framework/Mono",
            Platform = "Unity Mono",
        };

        Assert.Equal("BepInEx", host.Name);
        Assert.Equal("5.x", host.Version);
        Assert.Equal(".NET Framework/Mono", host.Runtime);
        Assert.Equal("Unity Mono", host.Platform);
    }
}
```

Extend `RoundTrip_HandshakeMessage` in `tests/HotRepl.Tests/Unit/MessageSerializerTests.cs` so it fails until nested metadata exists:

```csharp
[Fact]
public void RoundTrip_HandshakeMessage_IncludesEvaluatorAndHostMetadata()
{
    var msg = new HandshakeMessage
    {
        Version = "1.0.0",
        CsharpVersion = "7.x",
        DefaultUsings = new[] { "System", "System.Linq" },
        Helpers = new[] { "String[] Help()" },
        Evaluator = new EvaluatorCapabilities
        {
            Name = "Mono.CSharp",
            LanguageVersion = "7.x",
            SupportsPersistentState = true,
            SupportsCompletion = true,
            TimeoutMode = TimeoutMode.HardAbort,
        },
        Host = new HostInfo
        {
            Name = "BepInEx",
            Version = "5.x",
            Runtime = ".NET Framework/Mono",
            Platform = "Unity Mono",
        },
        AvailableEvaluators = new[] { "Mono.CSharp" },
    };

    var json = MessageSerializer.Serialize(msg);
    var back = MessageSerializer.Deserialize<HandshakeMessage>(json);

    Assert.Contains("\"evaluator\"", json);
    Assert.Contains("\"timeoutMode\":\"HardAbort\"", json);
    Assert.Contains("\"host\"", json);
    Assert.Contains("\"availableEvaluators\"", json);
    Assert.Equal("Mono.CSharp", back.Evaluator!.Name);
    Assert.Equal(TimeoutMode.HardAbort, back.Evaluator.TimeoutMode);
    Assert.Equal("BepInEx", back.Host!.Name);
    Assert.Equal(new[] { "Mono.CSharp" }, back.AvailableEvaluators);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run from `/Users/joaichberger/Projects/HotRepl`:

```bash
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q --filter "EvaluatorCapabilitiesTests|RoundTrip_HandshakeMessage_IncludesEvaluatorAndHostMetadata"
```

Expected: FAIL because `EvaluatorCapabilities`, `TimeoutMode`, `HostInfo`, and handshake metadata properties do not exist.

- [ ] **Step 3: Add public capability and host metadata types**

Create `src/HotRepl.Core/Evaluator/EvaluatorCapabilities.cs`:

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace HotRepl.Evaluator;

/// <summary>How an evaluator can enforce timeout and cancellation requests.</summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum TimeoutMode
{
    /// <summary>The engine can abort the main thread and recover the REPL session.</summary>
    HardAbort,

    /// <summary>The evaluator observes a cancellation token; runtime preemption is best effort.</summary>
    Cooperative,

    /// <summary>The evaluator cannot preempt execution.</summary>
    None,
}

/// <summary>Capability metadata reported by concrete evaluator implementations.</summary>
public sealed class EvaluatorCapabilities
{
    [JsonProperty("name")]
    public string Name { get; init; } = string.Empty;

    [JsonProperty("languageVersion")]
    public string LanguageVersion { get; init; } = string.Empty;

    [JsonProperty("supportsPersistentState")]
    public bool SupportsPersistentState { get; init; }

    [JsonProperty("supportsCompletion")]
    public bool SupportsCompletion { get; init; }

    [JsonProperty("timeoutMode")]
    public TimeoutMode TimeoutMode { get; init; }
}
```

Create `src/HotRepl.Core/HostInfo.cs`:

```csharp
using Newtonsoft.Json;

namespace HotRepl;

/// <summary>Metadata describing the host adapter that embedded the REPL engine.</summary>
public sealed class HostInfo
{
    [JsonProperty("name")]
    public string Name { get; init; } = string.Empty;

    [JsonProperty("version")]
    public string Version { get; init; } = string.Empty;

    [JsonProperty("runtime")]
    public string Runtime { get; init; } = string.Empty;

    [JsonProperty("platform")]
    public string Platform { get; init; } = string.Empty;
}
```

- [ ] **Step 4: Make evaluator contract public and pass cancellation tokens**

Modify `src/HotRepl.Core/Evaluator/ICodeEvaluator.cs`:

```csharp
using System.Reflection;
using System.Threading;

namespace HotRepl.Evaluator;

/// <summary>
/// Compiles and executes C# code at runtime.
/// All methods MUST be called from the main thread that drives <see cref="ReplEngine.Tick"/>.
/// </summary>
public interface ICodeEvaluator : System.IDisposable
{
    bool IsInitialized { get; }
    EvaluatorCapabilities Capabilities { get; }
    bool PendingHotReload { get; }
    string? PendingHotReloadAssembly { get; }

    void Initialize();
    EvalOutcome Evaluate(string code, CancellationToken cancellationToken);
    CompletionResult Complete(string code, int cursorPos);
    void Reset();
    void ReferenceAssembly(Assembly assembly);
    void RunInternal(string code);
}
```

Modify `src/HotRepl.Core/Evaluator/EvalOutcome.cs` and `src/HotRepl.Core/Evaluator/CompletionResult.cs` by changing their type declarations from `internal sealed class` to `public sealed class`. Leave existing factory methods and properties unchanged.

Modify `src/HotRepl.Core/Evaluator/EvalJob.cs` to own a cancellation source:

```csharp
using System;
using System.Threading;

namespace HotRepl.Evaluator;

internal sealed class EvalJob : IDisposable
{
    public string Id { get; }
    public string Code { get; }
    public int TimeoutMs { get; }
    public Guid ConnectionId { get; }
    public CancellationTokenSource Cancellation { get; } = new();

    public EvalJob(string id, string code, int timeoutMs, Guid connectionId)
    {
        Id = id;
        Code = code;
        TimeoutMs = timeoutMs;
        ConnectionId = connectionId;
    }

    public void Dispose() => Cancellation.Dispose();
}
```

- [ ] **Step 5: Add evaluator factory metadata to host contract and config**

Modify `src/HotRepl.Core/ReplConfig.cs` by adding this property after `Port`:

```csharp
/// <summary>
/// Optional evaluator name override. When null, the host chooses its safe default.
/// </summary>
public string? DefaultEvaluatorName { get; set; }
```

Modify `src/HotRepl.Core/IReplHost.cs` with the shared contract shown earlier in this plan. Keep XML comments on all public members. The new comments should state:

```csharp
/// <summary>Metadata about the embedding host, reported in the protocol handshake.</summary>
HostInfo HostInfo { get; }

/// <summary>Evaluators this host can construct in the current runtime.</summary>
IReadOnlyList<HotRepl.Evaluator.EvaluatorCapabilities> AvailableEvaluators { get; }

/// <summary>Name of the evaluator to use when the engine starts.</summary>
string DefaultEvaluatorName { get; }

/// <summary>Create a fresh evaluator instance by capability name.</summary>
HotRepl.Evaluator.ICodeEvaluator CreateEvaluator(string evaluatorName);
```

- [ ] **Step 6: Give Mono.CSharp capabilities while it still lives in Core**

Modify `src/HotRepl.Core/Evaluator/MonoCSharpEvaluator.cs`:

```csharp
public static readonly EvaluatorCapabilities MonoCapabilities = new()
{
    Name = "Mono.CSharp",
    LanguageVersion = "7.x",
    SupportsPersistentState = true,
    SupportsCompletion = true,
    TimeoutMode = TimeoutMode.HardAbort,
};

public EvaluatorCapabilities Capabilities => MonoCapabilities;

public EvalOutcome Evaluate(string code, CancellationToken cancellationToken)
{
    _ = cancellationToken;
    // existing Evaluate body follows unchanged
}
```

Add `using System.Threading;` if it is not already present.

- [ ] **Step 7: Update BepInEx host to provide evaluator factories and host info**

Modify `src/HotRepl.BepInEx/BepInExHost.cs`:

```csharp
using HotRepl.Evaluator;
```

Add members:

```csharp
private static readonly EvaluatorCapabilities[] _availableEvaluators =
{
    MonoCSharpEvaluator.MonoCapabilities,
};

public HostInfo HostInfo { get; } = new()
{
    Name = "BepInEx",
    Version = "5.x",
    Runtime = ".NET Framework/Mono",
    Platform = "Unity Mono",
};

public IReadOnlyList<EvaluatorCapabilities> AvailableEvaluators => _availableEvaluators;

public string DefaultEvaluatorName =>
    Config.DefaultEvaluatorName ?? MonoCSharpEvaluator.MonoCapabilities.Name;

public ICodeEvaluator CreateEvaluator(string evaluatorName)
{
    if (evaluatorName == MonoCSharpEvaluator.MonoCapabilities.Name)
        return new MonoCSharpEvaluator(this);

    throw new NotSupportedException($"Evaluator '{evaluatorName}' is not available in this host.");
}
```

- [ ] **Step 8: Add metadata fields to handshake message**

Modify `src/HotRepl.Core/Protocol/Messages.cs`:

```csharp
using HotRepl;
using HotRepl.Evaluator;
```

Extend `HandshakeMessage`:

```csharp
[JsonProperty("evaluator")] public EvaluatorCapabilities? Evaluator { get; set; }
[JsonProperty("host")] public HostInfo? Host { get; set; }
[JsonProperty("availableEvaluators")] public string[] AvailableEvaluators { get; set; } = Array.Empty<string>();
```

- [ ] **Step 9: Construct the active evaluator through the host and report metadata**

Modify `src/HotRepl.Core/ReplEngine.cs`:

- In `Start`, replace `_evaluator = new MonoCSharpEvaluator(_host);` with:

```csharp
_evaluator = CreateEvaluator(_host.DefaultEvaluatorName);
```

- Add helper:

```csharp
private ICodeEvaluator CreateEvaluator(string evaluatorName)
{
    var available = _host.AvailableEvaluators.Select(c => c.Name).ToArray();
    if (!available.Contains(evaluatorName, StringComparer.Ordinal))
        throw new NotSupportedException(
            $"Evaluator '{evaluatorName}' is not available. Available: {string.Join(", ", available)}");

    return _host.CreateEvaluator(evaluatorName);
}
```

- In `RunGuarded`, call `_evaluator!.Evaluate(code, CancellationToken.None)` for now. The cooperative token path is added in Task 4.
- In `OnClientConnected`, remove `MonoCSharpEvaluator.DefaultUsings` and use evaluator capabilities:

```csharp
var usings = _host.AdditionalUsings.ToArray();
var helpers = HelperInjector.AllHelperSignatures(_host);

_clients.Send(MessageSerializer.Serialize(new HandshakeMessage
{
    Version = "1.0.0",
    CsharpVersion = _evaluator?.Capabilities.LanguageVersion ?? "unknown",
    Evaluator = _evaluator?.Capabilities,
    Host = _host.HostInfo,
    AvailableEvaluators = _host.AvailableEvaluators.Select(e => e.Name).ToArray(),
    DefaultUsings = usings,
    Helpers = helpers,
}));
```

Then preserve current default using visibility by adding default using names to the host in Step 10.

- [ ] **Step 10: Preserve default using reporting for Mono**

Modify `BepInExHost._additionalUsings` to include Mono default usings plus Unity helper namespace:

```csharp
private static readonly IReadOnlyList<string> _additionalUsings =
    MonoCSharpEvaluator.DefaultUsings
        .Concat(new[] { "HotRepl.BepInEx.Helpers" })
        .ToArray();
```

This is a temporary shape. Task 3 changes the helper namespace to `HotRepl.Helpers.Unity`.

- [ ] **Step 11: Run unit tests**

```bash
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q --filter "EvaluatorCapabilitiesTests|MessageSerializerTests"
```

Expected: PASS.

- [ ] **Step 12: Run Core build and format**

```bash
dotnet build src/HotRepl.Core/HotRepl.Core.csproj --nologo -v q
dotnet format src/HotRepl.Core/HotRepl.Core.csproj --verify-no-changes --no-restore
```

Expected: both PASS.

- [ ] **Step 13: Commit**

```bash
git add src/HotRepl.Core src/HotRepl.BepInEx tests/HotRepl.Tests
git commit -m "feat(core): report evaluator and host capabilities"
```

Commit body:

```text
The engine needs an explicit runtime contract before evaluators can move
out of Core. Adding capability and host metadata makes timeout semantics
and available evaluators observable instead of inferred from the adapter.
```

---

### Task 2: Extract Mono.CSharp evaluator from Core

**Files:**
- Create: `src/HotRepl.Evaluator.MonoCSharp/HotRepl.Evaluator.MonoCSharp.csproj`
- Create: `src/HotRepl.Evaluator.MonoCSharp/MonoCSharpEvaluator.cs`
- Create: `src/HotRepl.Evaluator.MonoCSharp/AssemblyFilter.cs`
- Modify: `src/HotRepl.Core/HotRepl.Core.csproj`
- Modify: `src/HotRepl.BepInEx/HotRepl.BepInEx.csproj`
- Modify: `src/HotRepl.BepInEx/BepInExHost.cs`
- Modify: `tests/HotRepl.Tests/HotRepl.Tests.csproj`
- Modify: `tests/HotRepl.Tests/Unit/AssemblyFilterTests.cs`
- Modify: `HotRepl.slnx`

- [ ] **Step 1: Write failing project-boundary test by changing test namespace imports**

Modify `tests/HotRepl.Tests/Unit/AssemblyFilterTests.cs`:

```csharp
using HotRepl.Evaluator.MonoCSharp;
using Xunit;

namespace HotRepl.Tests.Unit;

public class AssemblyFilterTests
{
    [Theory]
    [InlineData("mscorlib")]
    [InlineData("System")]
    [InlineData("System.Core")]
    [InlineData("System.Xml")]
    [InlineData("completions")]
    [InlineData("netstandard")]
    public void IsFiltered_StdlibAndArtifacts(string name)
    {
        Assert.True(AssemblyFilter.IsFiltered(name));
    }

    [Theory]
    [InlineData("MSCORLIB")]
    [InlineData("system")]
    [InlineData("System.CORE")]
    public void IsFiltered_CaseInsensitive(string name)
    {
        Assert.True(AssemblyFilter.IsFiltered(name));
    }

    [Theory]
    [InlineData("UnityEngine")]
    [InlineData("Newtonsoft.Json")]
    [InlineData("HotRepl.Core")]
    public void IsFiltered_ReturnsFalse_ForNonStdLib(string name)
    {
        Assert.False(AssemblyFilter.IsFiltered(name));
    }
}
```

Run:

```bash
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q --filter AssemblyFilterTests
```

Expected: FAIL because `HotRepl.Evaluator.MonoCSharp` does not exist.

- [ ] **Step 2: Create the Mono evaluator project**

Create `src/HotRepl.Evaluator.MonoCSharp/HotRepl.Evaluator.MonoCSharp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AnalysisMode>Recommended</AnalysisMode>
    <RootNamespace>HotRepl.Evaluator.MonoCSharp</RootNamespace>
    <AssemblyName>HotRepl.Evaluator.MonoCSharp</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../HotRepl.Core/HotRepl.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="mcs">
      <HintPath>../../lib/mcs.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Move Mono files and update namespaces**

Move these files without changing behavior:

```text
src/HotRepl.Core/Evaluator/MonoCSharpEvaluator.cs
  -> src/HotRepl.Evaluator.MonoCSharp/MonoCSharpEvaluator.cs

src/HotRepl.Core/Evaluator/AssemblyFilter.cs
  -> src/HotRepl.Evaluator.MonoCSharp/AssemblyFilter.cs
```

In both moved files, use this namespace:

```csharp
namespace HotRepl.Evaluator.MonoCSharp;
```

At the top of `MonoCSharpEvaluator.cs`, include Core evaluator types:

```csharp
using HotRepl.Evaluator;
```

Make `AssemblyFilter` public so tests and the Mono evaluator project can use it:

```csharp
public static class AssemblyFilter
```

Make `MonoCSharpEvaluator.DefaultUsings` public because the BepInEx host reports them:

```csharp
public static readonly string[] DefaultUsings =
```

- [ ] **Step 4: Remove mcs from Core and add project references**

Modify `src/HotRepl.Core/HotRepl.Core.csproj` by deleting the `mcs` reference item group:

```xml
<ItemGroup>
  <Reference Include="mcs">
    <HintPath>../../lib/mcs.dll</HintPath>
    <Private>false</Private>
  </Reference>
</ItemGroup>
```

Modify `src/HotRepl.BepInEx/HotRepl.BepInEx.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="../HotRepl.Core/HotRepl.Core.csproj" />
  <ProjectReference Include="../HotRepl.Evaluator.MonoCSharp/HotRepl.Evaluator.MonoCSharp.csproj" />
</ItemGroup>
```

Modify `tests/HotRepl.Tests/HotRepl.Tests.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="../../src/HotRepl.Core/HotRepl.Core.csproj" />
  <ProjectReference Include="../../src/HotRepl.Evaluator.MonoCSharp/HotRepl.Evaluator.MonoCSharp.csproj" />
</ItemGroup>
```

Keep the test project's `mcs.dll` copy item so Mono evaluator tests can load it.

- [ ] **Step 5: Update BepInEx host imports**

Modify `src/HotRepl.BepInEx/BepInExHost.cs`:

```csharp
using HotRepl.Evaluator;
using HotRepl.Evaluator.MonoCSharp;
```

No behavior changes should be needed beyond the namespace import.

- [ ] **Step 6: Add the project to the solution**

Modify `HotRepl.slnx`:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/HotRepl.BepInEx/HotRepl.BepInEx.csproj" />
    <Project Path="src/HotRepl.Core/HotRepl.Core.csproj" />
    <Project Path="src/HotRepl.Evaluator.MonoCSharp/HotRepl.Evaluator.MonoCSharp.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/HotRepl.Tests/HotRepl.Tests.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 7: Run boundary and regression tests**

```bash
dotnet build src/HotRepl.Core/HotRepl.Core.csproj --nologo -v q
dotnet build src/HotRepl.Evaluator.MonoCSharp/HotRepl.Evaluator.MonoCSharp.csproj --nologo -v q
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q --filter "AssemblyFilterTests|EvaluatorCapabilitiesTests|MessageSerializerTests"
```

Expected: all PASS.

- [ ] **Step 8: Build BepInEx adapter locally**

```bash
dotnet build src/HotRepl.BepInEx/HotRepl.BepInEx.csproj --nologo -v q
```

Expected: PASS on this workstation if `src/HotRepl.BepInEx/lib/UnityEngine*.dll` exists. If Unity DLLs are absent, record the exact missing reference and continue only with Core/test verification; do not claim BepInEx build was verified.

- [ ] **Step 9: Commit**

```bash
git add HotRepl.slnx src tests
git commit -m "refactor(core): extract Mono evaluator assembly"
```

Commit body:

```text
Core should own the evaluator contract rather than the Mono compiler stack.
Moving Mono.CSharp behind the same public contract preserves the existing
runtime behavior while allowing other evaluators to ship independently.
```

---

### Task 3: Extract Unity helpers from BepInEx adapter

**Files:**
- Create: `src/HotRepl.Helpers.Unity/HotRepl.Helpers.Unity.csproj`
- Create: `src/HotRepl.Helpers.Unity/UnityHelpers.cs`
- Modify: `src/HotRepl.BepInEx/HotRepl.BepInEx.csproj`
- Modify: `src/HotRepl.BepInEx/BepInExHost.cs`
- Modify: `src/HotRepl.BepInEx/ReplPlugin.cs`
- Modify: `src/HotRepl.Core/Helpers/Repl.cs`
- Modify: `HotRepl.slnx`

- [ ] **Step 1: Create Unity helper project**

Create `src/HotRepl.Helpers.Unity/HotRepl.Helpers.Unity.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AnalysisMode>Recommended</AnalysisMode>
    <RootNamespace>HotRepl.Helpers.Unity</RootNamespace>
    <AssemblyName>HotRepl.Helpers.Unity</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="UnityEngine">
      <HintPath>../HotRepl.BepInEx/lib/UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>../HotRepl.BepInEx/lib/UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Move UnityHelpers and update namespace**

Move:

```text
src/HotRepl.BepInEx/UnityHelpers.cs
  -> src/HotRepl.Helpers.Unity/UnityHelpers.cs
```

Modify the namespace in the moved file:

```csharp
namespace HotRepl.Helpers.Unity;
```

Make `Initialize` public so both BepInEx and MelonLoader hosts can set the coroutine host:

```csharp
public static void Initialize(MonoBehaviour host)
```

- [ ] **Step 3: Update BepInEx host references**

Modify `src/HotRepl.BepInEx/HotRepl.BepInEx.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="../HotRepl.Core/HotRepl.Core.csproj" />
  <ProjectReference Include="../HotRepl.Evaluator.MonoCSharp/HotRepl.Evaluator.MonoCSharp.csproj" />
  <ProjectReference Include="../HotRepl.Helpers.Unity/HotRepl.Helpers.Unity.csproj" />
</ItemGroup>
```

Modify `src/HotRepl.BepInEx/BepInExHost.cs` imports:

```csharp
using HotRepl.Helpers.Unity;
```

Change the helper namespace registration:

```csharp
private static readonly IReadOnlyList<string> _additionalUsings =
    MonoCSharpEvaluator.DefaultUsings
        .Concat(new[] { "HotRepl.Helpers.Unity" })
        .ToArray();
```

Modify `src/HotRepl.BepInEx/ReplPlugin.cs`:

```csharp
using HotRepl.Helpers.Unity;
```

Replace:

```csharp
Helpers.UnityHelpers.Initialize(this);
```

with:

```csharp
UnityHelpers.Initialize(this);
```

- [ ] **Step 4: Update Core helper comment**

Modify the class comment in `src/HotRepl.Core/Helpers/Repl.cs` so it says:

```csharp
/// Platform-agnostic (no BepInEx or UnityEngine references at compile time).
/// Unity-specific helpers are provided by HotRepl.Helpers.Unity.
```

- [ ] **Step 5: Add helper project to solution**

Modify `HotRepl.slnx`:

```xml
<Project Path="src/HotRepl.Helpers.Unity/HotRepl.Helpers.Unity.csproj" />
```

Place it under `/src/` with the other projects.

- [ ] **Step 6: Verify builds**

```bash
dotnet build src/HotRepl.Core/HotRepl.Core.csproj --nologo -v q
dotnet build src/HotRepl.Helpers.Unity/HotRepl.Helpers.Unity.csproj --nologo -v q
dotnet build src/HotRepl.BepInEx/HotRepl.BepInEx.csproj --nologo -v q
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q --filter "MessageSerializerTests|EvaluatorCapabilitiesTests"
```

Expected: all available builds PASS. If Unity DLL references are absent, record the exact failing project and do not mark Unity/BepInEx build verified.

- [ ] **Step 7: Commit**

```bash
git add HotRepl.slnx src tests
git commit -m "refactor(bepinex): move Unity helpers to shared assembly"
```

Commit body:

```text
Unity helper methods are useful to more than the BepInEx adapter. Moving
them into a host-neutral helper assembly keeps Core clean and lets future
hosts inject the same helper surface without depending on BepInEx.
```

---

### Task 4: Add evaluator selection protocol and cooperative timeout plumbing

**Files:**
- Create: `src/HotRepl.Core/Evaluator/SelectEvaluatorCommand.cs`
- Create: `tests/HotRepl.Tests/Unit/SelectEvaluatorProtocolTests.cs`
- Modify: `src/HotRepl.Core/Protocol/Messages.cs`
- Modify: `src/HotRepl.Core/Server/MessageRouter.cs`
- Modify: `src/HotRepl.Core/ReplEngine.cs`
- Modify: `src/HotRepl.Core/Evaluator/EvalJob.cs`
- Modify: `src/HotRepl.Core/Subscriptions/SubscriptionManager.cs`
- Modify: `client/src/hotrepl/_client.py`
- Modify: `client/src/hotrepl/_types.py`
- Modify: `client/src/hotrepl/cli.py`

- [ ] **Step 1: Write failing select-evaluator protocol tests**

Create `tests/HotRepl.Tests/Unit/SelectEvaluatorProtocolTests.cs`:

```csharp
using HotRepl.Protocol;
using Xunit;

namespace HotRepl.Tests.Unit;

public class SelectEvaluatorProtocolTests
{
    [Fact]
    public void RoundTrip_SelectEvaluatorMessage()
    {
        var msg = new SelectEvaluatorMessage { Id = "s-1", Evaluator = "Roslyn.Script" };
        var json = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<SelectEvaluatorMessage>(json);

        Assert.Equal(MessageType.SelectEvaluator, back.Type);
        Assert.Equal("s-1", back.Id);
        Assert.Equal("Roslyn.Script", back.Evaluator);
    }

    [Fact]
    public void RoundTrip_SelectEvaluatorResultMessage()
    {
        var msg = new SelectEvaluatorResultMessage
        {
            Id = "s-2",
            Success = true,
            Evaluator = "Mono.CSharp",
        };

        var back = MessageSerializer.Deserialize<SelectEvaluatorResultMessage>(
            MessageSerializer.Serialize(msg));

        Assert.Equal(MessageType.SelectEvaluatorResult, back.Type);
        Assert.True(back.Success);
        Assert.Equal("Mono.CSharp", back.Evaluator);
    }

    [Fact]
    public void RoundTrip_SelectEvaluatorErrorMessage()
    {
        var msg = new SelectEvaluatorErrorMessage
        {
            Id = "s-3",
            ErrorKind = ErrorKind.Unsupported,
            Message = "Evaluator is not available.",
        };

        var json = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<SelectEvaluatorErrorMessage>(json);

        Assert.Equal(MessageType.SelectEvaluatorError, back.Type);
        Assert.Equal("unsupported", back.ErrorKind);
        Assert.Equal("Evaluator is not available.", back.Message);
    }
}
```

Run:

```bash
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q --filter SelectEvaluatorProtocolTests
```

Expected: FAIL because the message types do not exist.

- [ ] **Step 2: Add protocol constants and messages**

Modify `src/HotRepl.Core/Protocol/Messages.cs`:

```csharp
// Inbound
public const string SelectEvaluator = "select_evaluator";

// Outbound
public const string SelectEvaluatorResult = "select_evaluator_result";
public const string SelectEvaluatorError = "select_evaluator_error";
```

Add unsupported error kind:

```csharp
public const string Unsupported = "unsupported";
```

Add messages:

```csharp
internal sealed class SelectEvaluatorMessage
{
    [JsonProperty("type")] public string Type { get; init; } = MessageType.SelectEvaluator;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("evaluator")] public string Evaluator { get; set; } = string.Empty;
}

internal sealed class SelectEvaluatorResultMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.SelectEvaluatorResult;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("success")] public bool Success { get; set; }
    [JsonProperty("evaluator")] public string Evaluator { get; set; } = string.Empty;
}

internal sealed class SelectEvaluatorErrorMessage
{
    [JsonProperty("type")] public string Type { get; } = MessageType.SelectEvaluatorError;
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("errorKind")] public string ErrorKind { get; set; } = string.Empty;
    [JsonProperty("message")] public string Message { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Add command value and route messages**

Create `src/HotRepl.Core/Evaluator/SelectEvaluatorCommand.cs`:

```csharp
using System;

namespace HotRepl.Evaluator;

internal sealed class SelectEvaluatorCmd : IEngineCommand
{
    public string Id { get; }
    public string Evaluator { get; }
    public Guid ConnectionId { get; }

    public SelectEvaluatorCmd(string id, string evaluator, Guid connectionId)
    {
        Id = id;
        Evaluator = evaluator;
        ConnectionId = connectionId;
    }
}
```

Modify `src/HotRepl.Core/Server/MessageRouter.cs` switch:

```csharp
case MessageType.SelectEvaluator:
    {
        var msg = MessageSerializer.Deserialize<SelectEvaluatorMessage>(rawJson);
        _engine.EnqueueCommand(new SelectEvaluatorCmd(msg.Id, msg.Evaluator, connectionId));
        break;
    }
```

- [ ] **Step 4: Implement evaluator switching as a reset boundary**

Modify `src/HotRepl.Core/ReplEngine.cs`:

- In `HandleCommand`, add:

```csharp
case SelectEvaluatorCmd s:
    HandleSelectEvaluator(s);
    break;
```

- Add method:

```csharp
private void HandleSelectEvaluator(SelectEvaluatorCmd cmd)
{
    if (!_host.AvailableEvaluators.Any(e => e.Name == cmd.Evaluator))
    {
        _clients!.SendTo(cmd.ConnectionId, MessageSerializer.Serialize(new SelectEvaluatorErrorMessage
        {
            Id = cmd.Id,
            ErrorKind = ErrorKind.Unsupported,
            Message = $"Evaluator '{cmd.Evaluator}' is not available. Available: "
                + string.Join(", ", _host.AvailableEvaluators.Select(e => e.Name)),
        }));
        return;
    }

    foreach (var sub in GetAllSubscriptions())
    {
        _clients!.SendTo(sub.ConnectionId, MessageSerializer.Serialize(new SubscribeErrorMessage
        {
            Id = sub.Id,
            Seq = sub.Seq + 1,
            ErrorKind = ErrorKind.Cancelled,
            Message = "Evaluator selection changed.",
            Final = true,
        }));
    }
    _subscriptions!.CancelAll();

    while (_evalQueue.TryDequeue(out var job))
    {
        using (job)
        {
            _clients!.SendTo(job.ConnectionId, MessageSerializer.Serialize(new EvalErrorMessage
            {
                Id = job.Id,
                ErrorKind = ErrorKind.Cancelled,
                Message = "Evaluator selection changed.",
            }));
        }
    }

    _evaluator?.Dispose();
    _evaluator = CreateEvaluator(cmd.Evaluator);
    _evaluatorReady = false;
    InitializeEvaluator();
    _evaluatorReady = true;

    _clients!.SendTo(cmd.ConnectionId, MessageSerializer.Serialize(new SelectEvaluatorResultMessage
    {
        Id = cmd.Id,
        Success = true,
        Evaluator = _evaluator.Capabilities.Name,
    }));

    _host.LogInfo($"[HotRepl] Evaluator selected: {_evaluator.Capabilities.Name}.");
}
```

- [ ] **Step 5: Implement cooperative cancellation path**

Modify `RunGuarded` in `src/HotRepl.Core/ReplEngine.cs`:

```csharp
private EvalOutcome GuardedEvaluate(string id, string code, int timeoutMs)
{
    using var cts = new CancellationTokenSource();
    return RunGuarded(id, code, timeoutMs, cts);
}

private EvalOutcome RunGuarded(string id, string code, int timeoutMs, CancellationTokenSource cancellation)
```

Inside watchdog callback, replace unconditional abort with evaluator-aware behavior:

```csharp
_timedOut = true;
if (_evaluator!.Capabilities.TimeoutMode == TimeoutMode.HardAbort)
    _mainThread?.Abort();
else if (_evaluator.Capabilities.TimeoutMode == TimeoutMode.Cooperative)
    cancellation.Cancel();
```

Call the evaluator with the token:

```csharp
var outcome = _evaluator!.Evaluate(code, cancellation.Token);
```

Keep the existing `EvalOutcome.Aborted` resolution logic unchanged.

Modify queued eval execution:

```csharp
private void ExecuteEval(EvalJob job)
{
    using (job)
    {
        EvalOutcome outcome = RunGuarded(job.Id, job.Code, job.TimeoutMs, job.Cancellation);
        RecordHistory(job.Code, outcome);
        SendEvalOutcome(job.Id, job.ConnectionId, outcome);
    }
}
```

Modify `CancelEval`:

```csharp
if (_evalInProgress && _currentEvalId == id)
{
    if (_evaluator?.Capabilities.TimeoutMode == TimeoutMode.HardAbort)
        _mainThread?.Abort();
}
```

The queued job's token handles cancellation before execution. Runtime cooperative cancellation for the active eval is done by the watchdog token; a manual cancel for a cooperative evaluator should be wired in the next small edit by storing the active cancellation source:

```csharp
private CancellationTokenSource? _currentCancellation;
```

Set it under `_abortLock` before evaluation and clear it in `finally`. Then `CancelEval` calls `_currentCancellation?.Cancel()` for cooperative evaluators.

- [ ] **Step 6: Preserve subscription timeout behavior**

Modify `src/HotRepl.Core/Subscriptions/SubscriptionManager.cs` delegate type from:

```csharp
Func<string, string, int, EvalOutcome> guardedEvaluate
```

to:

```csharp
Func<string, string, int, EvalOutcome> guardedEvaluate
```

No signature change is needed at the subscription boundary because `GuardedEvaluate` creates its own cancellation source. Verify the file still compiles.

- [ ] **Step 7: Add Python client select support**

Modify `client/src/hotrepl/_types.py`:

```python
class EvaluatorMetadata(TypedDict, total=False):
    name: str
    languageVersion: str
    supportsPersistentState: bool
    supportsCompletion: bool
    timeoutMode: str


class HostMetadata(TypedDict, total=False):
    name: str
    version: str
    runtime: str
    platform: str


class Handshake(TypedDict, total=False):
    type: Required[str]
    version: str
    csharpVersion: str
    evaluator: EvaluatorMetadata
    host: HostMetadata
    availableEvaluators: list[str]
    defaultUsings: list[str]
    helpers: list[str]
```

Modify `client/src/hotrepl/_client.py`:

```python
    async def select_evaluator(self, evaluator: str) -> dict[str, Any]:
        """Select a server-side evaluator by advertised capability name."""
        msg_id = self._next_id()
        payload = {"type": "select_evaluator", "id": msg_id, "evaluator": evaluator}
        resp = await self._request(payload, msg_id)
        if resp.get("type") == "select_evaluator_error":
            raise EvalError(
                message=resp.get("message", "Evaluator selection failed"),
                kind=resp.get("errorKind", "unsupported"),
                stack_trace=None,
            )
        return resp
```

Update `_request` so it raises `EvalError` for both `eval_error` and `select_evaluator_error`:

```python
if resp.get("type") in {"eval_error", "select_evaluator_error"}:
    raise EvalError(
        message=resp.get("message", resp.get("error", "unknown")),
        kind=resp.get("errorKind", "unknown"),
        stack_trace=resp.get("stackTrace"),
    )
```

Modify `client/src/hotrepl/cli.py`:

```python
async def _cmd_info(args: argparse.Namespace) -> None:
    async with Client(_get_url(args)) as client:
        handshake = client.handshake or {}
    print(json.dumps(handshake, indent=2) if args.json else _format_info(handshake))


def _format_info(handshake: dict[str, Any]) -> str:
    evaluator = handshake.get("evaluator") or {}
    host = handshake.get("host") or {}
    available = handshake.get("availableEvaluators") or []
    return "\n".join(
        [
            f"host: {host.get('name', 'unknown')} {host.get('version', '')}".rstrip(),
            f"runtime: {host.get('runtime', 'unknown')}",
            f"platform: {host.get('platform', 'unknown')}",
            f"evaluator: {evaluator.get('name', 'unknown')}",
            f"language: {evaluator.get('languageVersion', handshake.get('csharpVersion', 'unknown'))}",
            f"timeout: {evaluator.get('timeoutMode', 'unknown')}",
            "available evaluators: " + ", ".join(available),
        ]
    )


async def _cmd_select_evaluator(args: argparse.Namespace) -> None:
    async with Client(_get_url(args)) as client:
        result = await client.select_evaluator(args.evaluator)
    print(json.dumps(result, indent=2) if args.json else f"selected: {result.get('evaluator')}")
```

Add parsers:

```python
p_info = sub.add_parser("info", help="Show server host and evaluator metadata")
p_info.add_argument("--json", action="store_true", help="Output raw JSON")

p_select = sub.add_parser("select-evaluator", help="Select server evaluator")
p_select.add_argument("evaluator", help="Evaluator name from handshake.availableEvaluators")
p_select.add_argument("--json", action="store_true", help="Output raw JSON")
```

Add dispatch entries:

```python
"info": _cmd_info,
"select-evaluator": _cmd_select_evaluator,
```

- [ ] **Step 8: Run C# and Python checks**

```bash
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q --filter "SelectEvaluatorProtocolTests|MessageSerializerTests|EvaluatorCapabilitiesTests"
```

Expected: PASS.

```bash
cd client
uv run ruff check src/ tests/
uv run mypy src/hotrepl/
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src tests client
git commit -m "feat(protocol): support evaluator selection"
```

Commit body:

```text
Multiple evaluator assemblies need a protocol-visible selection boundary.
The command treats evaluator changes as a reset boundary so queued work and
subscriptions cannot silently cross into a different compiler session.
```

---

### Task 5: Add Roslyn Script evaluator

**Files:**
- Create: `src/HotRepl.Evaluator.Roslyn/HotRepl.Evaluator.Roslyn.csproj`
- Create: `src/HotRepl.Evaluator.Roslyn/RoslynScriptEvaluator.cs`
- Create: `src/HotRepl.Evaluator.Roslyn/RoslynEvaluatorFactory.cs`
- Create: `tests/HotRepl.Tests/Unit/RoslynScriptEvaluatorTests.cs`
- Modify: `src/HotRepl.BepInEx/HotRepl.BepInEx.csproj`
- Modify: `src/HotRepl.BepInEx/BepInExHost.cs`
- Modify: `tests/HotRepl.Tests/HotRepl.Tests.csproj`
- Modify: `HotRepl.slnx`
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Write failing Roslyn script evaluator tests**

Create `tests/HotRepl.Tests/Unit/RoslynScriptEvaluatorTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HotRepl.Evaluator;
using HotRepl.Evaluator.Roslyn;
using Xunit;

namespace HotRepl.Tests.Unit;

public class RoslynScriptEvaluatorTests
{
    private sealed class TestHost : IReplHost
    {
        public ReplConfig Config { get; } = new();
        public HostInfo HostInfo { get; } = new() { Name = "Tests", Version = "1", Runtime = ".NET", Platform = "Unit" };
        public IReadOnlyList<EvaluatorCapabilities> AvailableEvaluators => RoslynEvaluatorFactory.Capabilities;
        public string DefaultEvaluatorName => RoslynScriptEvaluator.ScriptCapabilities.Name;
        public ICodeEvaluator CreateEvaluator(string evaluatorName) => RoslynEvaluatorFactory.Create(evaluatorName, this);
        public IReadOnlyList<Assembly> AdditionalAssemblies { get; } = Array.Empty<Assembly>();
        public IReadOnlyList<string> AdditionalUsings { get; } = Array.Empty<string>();
        public string[] AdditionalHelperSignatures { get; } = Array.Empty<string>();
        public void LogInfo(string message) { }
        public void LogDebug(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message, Exception? ex = null) { }
    }

    [Fact]
    public void Capabilities_AreCooperativeAndPersistent()
    {
        Assert.Equal("Roslyn.Script", RoslynScriptEvaluator.ScriptCapabilities.Name);
        Assert.True(RoslynScriptEvaluator.ScriptCapabilities.SupportsPersistentState);
        Assert.False(RoslynScriptEvaluator.ScriptCapabilities.SupportsCompletion);
        Assert.Equal(TimeoutMode.Cooperative, RoslynScriptEvaluator.ScriptCapabilities.TimeoutMode);
    }

    [Fact]
    public void Evaluate_ReturnsValue()
    {
        using var evaluator = new RoslynScriptEvaluator(new TestHost());
        evaluator.Initialize();

        var result = evaluator.Evaluate("1 + 1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.HasValue);
        Assert.Equal(2, result.Value);
        Assert.Equal("System.Int32", result.ValueType);
    }

    [Fact]
    public void Evaluate_PreservesStateAcrossRuns()
    {
        using var evaluator = new RoslynScriptEvaluator(new TestHost());
        evaluator.Initialize();

        var first = evaluator.Evaluate("var x = 40;", CancellationToken.None);
        var second = evaluator.Evaluate("x + 2", CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(42, second.Value);
    }

    [Fact]
    public void Reset_DropsState()
    {
        using var evaluator = new RoslynScriptEvaluator(new TestHost());
        evaluator.Initialize();
        Assert.True(evaluator.Evaluate("var x = 1;", CancellationToken.None).Success);

        evaluator.Reset();
        var result = evaluator.Evaluate("x", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("compile", result.ErrorKind);
    }

    [Fact]
    public void Evaluate_ReportsCompileError()
    {
        using var evaluator = new RoslynScriptEvaluator(new TestHost());
        evaluator.Initialize();

        var result = evaluator.Evaluate("int x = ;", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("compile", result.ErrorKind);
        Assert.Contains("CS", result.ErrorMessage);
    }

    [Fact]
    public void Evaluate_ReportsRuntimeError()
    {
        using var evaluator = new RoslynScriptEvaluator(new TestHost());
        evaluator.Initialize();

        var result = evaluator.Evaluate("throw new InvalidOperationException(\"boom\");", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("runtime", result.ErrorKind);
        Assert.Contains("boom", result.ErrorMessage);
    }
}
```

Run:

```bash
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q --filter RoslynScriptEvaluatorTests
```

Expected: FAIL because the Roslyn project and types do not exist.

- [ ] **Step 2: Create Roslyn evaluator project**

Create `src/HotRepl.Evaluator.Roslyn/HotRepl.Evaluator.Roslyn.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>netstandard2.1;net6.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AnalysisMode>Recommended</AnalysisMode>
    <RootNamespace>HotRepl.Evaluator.Roslyn</RootNamespace>
    <AssemblyName>HotRepl.Evaluator.Roslyn</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../HotRepl.Core/HotRepl.Core.csproj" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Scripting" Version="4.14.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Implement Roslyn script evaluator**

Create `src/HotRepl.Evaluator.Roslyn/RoslynScriptEvaluator.cs`:

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HotRepl.Evaluator;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace HotRepl.Evaluator.Roslyn;

public sealed class RoslynScriptEvaluator : ICodeEvaluator
{
    public static readonly EvaluatorCapabilities ScriptCapabilities = new()
    {
        Name = "Roslyn.Script",
        LanguageVersion = "latest",
        SupportsPersistentState = true,
        SupportsCompletion = false,
        TimeoutMode = TimeoutMode.Cooperative,
    };

    private static readonly string[] DefaultImports =
    {
        "System",
        "System.Collections",
        "System.Collections.Generic",
        "System.Linq",
        "System.Reflection",
    };

    private readonly IReplHost _host;
    private ScriptOptions _options = ScriptOptions.Default;
    private ScriptState<object>? _state;
    private bool _isInitialized;
    private bool _disposed;

    public RoslynScriptEvaluator(IReplHost host) => _host = host;

    public bool IsInitialized => _isInitialized;
    public EvaluatorCapabilities Capabilities => ScriptCapabilities;
    public bool PendingHotReload => false;
    public string? PendingHotReloadAssembly => null;

    public void Initialize()
    {
        if (_isInitialized)
            return;

        _options = BuildOptions();
        _isInitialized = true;
    }

    public EvalOutcome Evaluate(string code, CancellationToken cancellationToken)
    {
        EnsureInitialized();
        var sw = Stopwatch.StartNew();
        var previousOut = Console.Out;
        using var capture = new StringWriter();
        Console.SetOut(capture);

        try
        {
            _state = _state == null
                ? CSharpScript.RunAsync(code, _options, cancellationToken: cancellationToken).GetAwaiter().GetResult()
                : _state.ContinueWithAsync(code, _options, cancellationToken).GetAwaiter().GetResult();

            sw.Stop();
            var value = _state.ReturnValue;
            return value != null
                ? EvalOutcome.Ok(value, value.GetType().FullName, Stdout(capture), sw.ElapsedMilliseconds)
                : EvalOutcome.OkVoid(Stdout(capture), sw.ElapsedMilliseconds);
        }
        catch (CompilationErrorException ex)
        {
            sw.Stop();
            return EvalOutcome.CompileError(string.Join(Environment.NewLine, ex.Diagnostics), Stdout(capture), sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return EvalOutcome.Aborted;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return EvalOutcome.RuntimeError(ex.Message, ex.StackTrace, Stdout(capture), sw.ElapsedMilliseconds);
        }
        finally
        {
            Console.SetOut(previousOut);
        }
    }

    public CompletionResult Complete(string code, int cursorPos) =>
        new(Array.Empty<string>(), 0);

    public void Reset()
    {
        _state = null;
        _options = BuildOptions();
        _isInitialized = true;
    }

    public void ReferenceAssembly(Assembly assembly)
    {
        if (CanReference(assembly))
            _options = _options.AddReferences(assembly);
    }

    public void RunInternal(string code)
    {
        EnsureInitialized();
        try
        {
            _state = _state == null
                ? CSharpScript.RunAsync(code, _options).GetAwaiter().GetResult()
                : _state.ContinueWithAsync(code, _options).GetAwaiter().GetResult();
        }
        catch
        {
            // Initialization imports are best-effort because Unity assemblies are
            // not present in unit-test runs.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _state = null;
    }

    private ScriptOptions BuildOptions()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Concat(_host.AdditionalAssemblies)
            .Where(CanReference)
            .Distinct()
            .ToArray();

        return ScriptOptions.Default
            .WithReferences(assemblies)
            .WithImports(DefaultImports.Concat(_host.AdditionalUsings));
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
            Initialize();
    }

    private static bool CanReference(Assembly assembly)
    {
        if (assembly.IsDynamic)
            return false;
        try
        {
            return !string.IsNullOrEmpty(assembly.Location);
        }
        catch
        {
            return false;
        }
    }

    private static string? Stdout(StringWriter writer)
    {
        var value = writer.ToString();
        return value.Length > 0 ? value : null;
    }
}
```

- [ ] **Step 4: Add Roslyn factory**

Create `src/HotRepl.Evaluator.Roslyn/RoslynEvaluatorFactory.cs`:

```csharp
using System;
using System.Collections.Generic;
using HotRepl.Evaluator;

namespace HotRepl.Evaluator.Roslyn;

public static class RoslynEvaluatorFactory
{
    private static readonly EvaluatorCapabilities[] _capabilities =
    {
        RoslynScriptEvaluator.ScriptCapabilities,
    };

    public static IReadOnlyList<EvaluatorCapabilities> Capabilities => _capabilities;

    public static ICodeEvaluator Create(string evaluatorName, IReplHost host)
    {
        if (evaluatorName == RoslynScriptEvaluator.ScriptCapabilities.Name)
            return new RoslynScriptEvaluator(host);

        throw new NotSupportedException($"Evaluator '{evaluatorName}' is not available in HotRepl.Evaluator.Roslyn.");
    }
}
```

Task 8 extends this factory for `Roslyn.Isolated` under `net6.0`.

- [ ] **Step 5: Reference Roslyn from tests and BepInEx host**

Modify `tests/HotRepl.Tests/HotRepl.Tests.csproj`:

```xml
<ProjectReference Include="../../src/HotRepl.Evaluator.Roslyn/HotRepl.Evaluator.Roslyn.csproj" />
```

Modify `src/HotRepl.BepInEx/HotRepl.BepInEx.csproj`:

```xml
<ProjectReference Include="../HotRepl.Evaluator.Roslyn/HotRepl.Evaluator.Roslyn.csproj" />
```

Modify `src/HotRepl.BepInEx/BepInExHost.cs`:

```csharp
using HotRepl.Evaluator.Roslyn;
```

Set available evaluators:

```csharp
private static readonly EvaluatorCapabilities[] _availableEvaluators =
    new[] { MonoCSharpEvaluator.MonoCapabilities }
        .Concat(RoslynEvaluatorFactory.Capabilities)
        .ToArray();
```

Extend factory:

```csharp
if (evaluatorName == RoslynScriptEvaluator.ScriptCapabilities.Name)
    return RoslynEvaluatorFactory.Create(evaluatorName, this);
```

Keep BepInEx default as Mono.CSharp:

```csharp
public string DefaultEvaluatorName =>
    Config.DefaultEvaluatorName ?? MonoCSharpEvaluator.MonoCapabilities.Name;
```

- [ ] **Step 6: Add project to solution and CI**

Modify `HotRepl.slnx` under `/src/`:

```xml
<Project Path="src/HotRepl.Evaluator.Roslyn/HotRepl.Evaluator.Roslyn.csproj" />
```

Modify `.github/workflows/ci.yml` C# restore/build steps:

```yaml
- name: Restore
  run: |
    dotnet restore src/HotRepl.Core/HotRepl.Core.csproj
    dotnet restore src/HotRepl.Evaluator.MonoCSharp/HotRepl.Evaluator.MonoCSharp.csproj
    dotnet restore src/HotRepl.Evaluator.Roslyn/HotRepl.Evaluator.Roslyn.csproj
    dotnet restore tests/HotRepl.Tests/HotRepl.Tests.csproj

- name: Build
  run: |
    dotnet build src/HotRepl.Core/HotRepl.Core.csproj --no-restore
    dotnet build src/HotRepl.Evaluator.MonoCSharp/HotRepl.Evaluator.MonoCSharp.csproj --no-restore
    dotnet build src/HotRepl.Evaluator.Roslyn/HotRepl.Evaluator.Roslyn.csproj --no-restore
    dotnet build tests/HotRepl.Tests/HotRepl.Tests.csproj --no-restore
```

- [ ] **Step 7: Run Roslyn tests**

```bash
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q --filter RoslynScriptEvaluatorTests
```

Expected: PASS.

- [ ] **Step 8: Run Core/evaluator build matrix**

```bash
dotnet build src/HotRepl.Core/HotRepl.Core.csproj --nologo -v q
dotnet build src/HotRepl.Evaluator.MonoCSharp/HotRepl.Evaluator.MonoCSharp.csproj --nologo -v q
dotnet build src/HotRepl.Evaluator.Roslyn/HotRepl.Evaluator.Roslyn.csproj --nologo -v q
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add .github HotRepl.slnx src tests
git commit -m "feat(evaluator): add Roslyn script evaluator"
```

Commit body:

```text
IL2CPP hosts cannot use Mono.CSharp, but they can run managed Roslyn
code from the mod side. The script evaluator adds a cooperative-timeout
compiler path while keeping BepInEx on Mono.CSharp by default.
```

---

### Task 6: Add MelonLoader host with Roslyn default

**Files:**
- Create: `src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj`
- Create: `src/HotRepl.Host.MelonLoader/MelonLoaderHost.cs`
- Create: `src/HotRepl.Host.MelonLoader/ReplMod.cs`
- Modify: `src/HotRepl.Core/HotRepl.Core.csproj`
- Modify: `HotRepl.slnx`
- Modify: `README.md`
- Modify: `AGENTS.md`

- [ ] **Step 1: Create MelonLoader host project**

Create `src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AnalysisMode>Recommended</AnalysisMode>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>HotRepl.Host.MelonLoader</RootNamespace>
    <AssemblyName>HotRepl.Host.MelonLoader</AssemblyName>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../HotRepl.Core/HotRepl.Core.csproj" />
    <ProjectReference Include="../HotRepl.Evaluator.Roslyn/HotRepl.Evaluator.Roslyn.csproj" />
    <ProjectReference Include="../HotRepl.Helpers.Unity/HotRepl.Helpers.Unity.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="MelonLoader">
      <HintPath>$(MelonLoaderPath)/net6/MelonLoader.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(Il2CppAssembliesPath)/UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Il2CppInterop.Runtime">
      <HintPath>$(MelonLoaderPath)/net6/Il2CppInterop.Runtime.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

This project intentionally uses MSBuild properties instead of a game path. Consumers pass `MelonLoaderPath` and `Il2CppAssembliesPath` from their local game configuration.

Modify `src/HotRepl.Core/HotRepl.Core.csproj` so the MelonLoader host can use the existing internal helper-signature builder:

```xml
<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
  <_Parameter1>HotRepl.Host.MelonLoader</_Parameter1>
</AssemblyAttribute>
```

- [ ] **Step 2: Implement MelonLoader host**

Create `src/HotRepl.Host.MelonLoader/MelonLoaderHost.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HotRepl.Evaluator;
using HotRepl.Evaluator.Roslyn;
using HotRepl.Helpers;
using HotRepl.Helpers.Unity;
using MelonLoader;

namespace HotRepl.Host.MelonLoader;

internal sealed class MelonLoaderHost : IReplHost
{
    private readonly MelonLogger.Instance _logger;

    private static readonly string[] _unityHelperSignatures =
        HelperInjector.BuildSignatures(typeof(UnityHelpers));

    private static readonly IReadOnlyList<Assembly> _additionalAssemblies =
        new[] { typeof(UnityHelpers).Assembly };

    private static readonly IReadOnlyList<string> _additionalUsings =
        new[] { "HotRepl.Helpers.Unity" };

    public MelonLoaderHost(MelonLogger.Instance logger, ReplConfig? config = null)
    {
        _logger = logger;
        Config = config ?? new ReplConfig();
    }

    public ReplConfig Config { get; }

    public HostInfo HostInfo { get; } = new()
    {
        Name = "MelonLoader",
        Version = "0.x",
        Runtime = ".NET 6",
        Platform = "Unity IL2CPP",
    };

    public IReadOnlyList<EvaluatorCapabilities> AvailableEvaluators => RoslynEvaluatorFactory.Capabilities;

    public string DefaultEvaluatorName =>
        Config.DefaultEvaluatorName ?? RoslynScriptEvaluator.ScriptCapabilities.Name;

    public ICodeEvaluator CreateEvaluator(string evaluatorName) =>
        RoslynEvaluatorFactory.Create(evaluatorName, this);

    public IReadOnlyList<Assembly> AdditionalAssemblies => _additionalAssemblies;
    public IReadOnlyList<string> AdditionalUsings => _additionalUsings;
    public string[] AdditionalHelperSignatures => _unityHelperSignatures;

    public void LogInfo(string message) => _logger.Msg(message);
    public void LogDebug(string message) => _logger.Msg(message);
    public void LogWarning(string message) => _logger.Warning(message);

    public void LogError(string message, Exception? ex = null)
    {
        if (ex != null)
            _logger.Error($"{message}\n{ex}");
        else
            _logger.Error(message);
    }
}
```

- [ ] **Step 3: Implement MelonLoader lifecycle entry point**

Create `src/HotRepl.Host.MelonLoader/ReplMod.cs`:

```csharp
using System;
using HotRepl.Helpers.Unity;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(HotRepl.Host.MelonLoader.ReplMod), "HotRepl", "0.1.0", "Oh My Pi")]

namespace HotRepl.Host.MelonLoader;

public sealed class ReplMod : MelonMod
{
    private ReplEngine? _engine;
    private MelonLoaderHost? _host;
    private CoroutineHostBehaviour? _coroutineHost;

    public override void OnInitializeMelon()
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<CoroutineHostBehaviour>();
            _coroutineHost = CreateCoroutineHost();
            UnityHelpers.Initialize(_coroutineHost);

            _host = new MelonLoaderHost(LoggerInstance);
            _engine = new ReplEngine(_host);
            _engine.Start();
            LoggerInstance.Msg($"HotRepl loaded — REPL on port {_host.Config.Port}.");
        }
        catch (Exception ex)
        {
            LoggerInstance.Error($"HotRepl failed to start: {ex}");
            _engine = null;
            _host = null;
            _coroutineHost = null;
        }
    }

    public override void OnUpdate()
    {
        if (_engine == null)
            return;

        try
        {
            _engine.Tick();
        }
        catch (Exception ex)
        {
            LoggerInstance.Error($"[HotRepl] Unhandled exception in Tick(): {ex}");
        }
    }

    public override void OnDeinitializeMelon()
    {
        _engine?.Dispose();
        _engine = null;
        _host = null;
        _coroutineHost = null;
    }

    private static CoroutineHostBehaviour CreateCoroutineHost()
    {
        var helperObject = new GameObject("HotRepl_CoroutineHost");
        UnityEngine.Object.DontDestroyOnLoad(helperObject);
        return helperObject.AddComponent<CoroutineHostBehaviour>();
    }

    public sealed class CoroutineHostBehaviour : MonoBehaviour
    {
        public CoroutineHostBehaviour(IntPtr ptr) : base(ptr) { }
    }
}
```

This coroutine host pattern matches the existing Ancient Kingdoms `MapScreenshotter` approach: register an IL2CPP `MonoBehaviour`, attach it to a persistent `GameObject`, and pass that component to `UnityHelpers.Initialize`. If the local MelonLoader API has a different registration requirement, update the code to the observed API before committing and record the observed API in the commit body.

- [ ] **Step 4: Validate MelonLoader API before finalizing coroutine host**

Use the local MelonLoader DLL from Ancient Kingdoms to inspect available lifecycle/coroutine APIs:

```bash
dotnet build src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj --nologo -v q -p:MelonLoaderPath="/Users/joaichberger/Library/Application Support/CrossOver/Bottles/Steam/drive_c/Program Files (x86)/Steam/steamapps/common/Ancient Kingdoms/MelonLoader" -p:Il2CppAssembliesPath="/Users/joaichberger/Library/Application Support/CrossOver/Bottles/Steam/drive_c/Program Files (x86)/Steam/steamapps/common/Ancient Kingdoms/MelonLoader/Il2CppAssemblies"
```

Expected first run: either PASS or a precise compile error showing the incorrect MelonLoader API surface. If there is an API mismatch, inspect the referenced MelonLoader assembly with `dotnet` tooling or C# reflection and update `ReplMod.cs` to the actual API. Do not guess.

- [ ] **Step 5: Add project to solution**

Modify `HotRepl.slnx` under `/src/`:

```xml
<Project Path="src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj" />
```

- [ ] **Step 6: Update docs with build command and side-by-side deploy layout**

In `README.md`, add a MelonLoader build section:

```markdown
### MelonLoader / IL2CPP

Build with game-provided paths:

```bash
dotnet build src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj \
  -p:MelonLoaderPath="/path/to/Game/MelonLoader" \
  -p:Il2CppAssembliesPath="/path/to/Game/MelonLoader/Il2CppAssemblies"
```

Deploy the host, Core, Roslyn evaluator, Unity helpers, Fleck, Newtonsoft.Json,
and Roslyn dependency DLLs side-by-side in the game's `Mods/` directory unless a
specific game documents a `UserLibs/` dependency layout. The MelonLoader host uses
Roslyn.Script by default and reports `timeoutMode: "Cooperative"` in the handshake.
```

In `AGENTS.md`, add a build note:

```markdown
### MelonLoader host

`src/HotRepl.Host.MelonLoader` is not built in CI because it requires game-local
MelonLoader and IL2CPP assemblies. On this workstation, pass `MelonLoaderPath` and
`Il2CppAssembliesPath` from the target game's install. Do not hard-code Ancient
Kingdoms paths in HotRepl source files.
```

- [ ] **Step 7: Verify non-game projects still pass**

```bash
dotnet build src/HotRepl.Core/HotRepl.Core.csproj --nologo -v q
dotnet build src/HotRepl.Evaluator.Roslyn/HotRepl.Evaluator.Roslyn.csproj --nologo -v q
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q
```

Expected: PASS.

- [ ] **Step 8: Verify MelonLoader host build with local game paths**

Run the command from Step 4 after API corrections. Expected: PASS. If it cannot pass because the local game path is missing, mark this task blocked and report the exact missing path.

- [ ] **Step 9: Commit**

```bash
git add HotRepl.slnx src/HotRepl.Host.MelonLoader README.md AGENTS.md
git commit -m "feat(melonloader): add Roslyn-backed host adapter"
```

Commit body:

```text
IL2CPP games need a host that runs managed mod-side code rather than the
Mono evaluator. The MelonLoader adapter wires the existing engine to the
Melon lifecycle and keeps game paths outside the repository.
```

---

### Task 7: Add IL2CPP helper assembly and MelonLoader injection

**Files:**
- Create: `src/HotRepl.Helpers.Il2Cpp/HotRepl.Helpers.Il2Cpp.csproj`
- Create: `src/HotRepl.Helpers.Il2Cpp/Il2CppHelpers.cs`
- Modify: `src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj`
- Modify: `src/HotRepl.Host.MelonLoader/MelonLoaderHost.cs`
- Modify: `HotRepl.slnx`
- Modify: `README.md`

- [ ] **Step 1: Create IL2CPP helper project**

Create `src/HotRepl.Helpers.Il2Cpp/HotRepl.Helpers.Il2Cpp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AnalysisMode>Recommended</AnalysisMode>
    <RootNamespace>HotRepl.Helpers.Il2Cpp</RootNamespace>
    <AssemblyName>HotRepl.Helpers.Il2Cpp</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(Il2CppAssembliesPath)/UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Il2CppInterop.Runtime">
      <HintPath>$(MelonLoaderPath)/net6/Il2CppInterop.Runtime.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Il2Cppmscorlib">
      <HintPath>$(Il2CppAssembliesPath)/Il2Cppmscorlib.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Implement helper methods**

Create `src/HotRepl.Helpers.Il2Cpp/Il2CppHelpers.cs`:

```csharp
using System;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace HotRepl.Helpers.Il2Cpp;

public static class Il2CppHelpers
{
    public static object[] FindObjects(string fullTypeName)
    {
        var wrapperType = ResolveManagedWrapperType(fullTypeName);
        var il2cppType = CreateIl2CppType(wrapperType);
        return Resources.FindObjectsOfTypeAll(il2cppType).Cast<object>().ToArray();
    }

    public static object DescribeType(string fullTypeName)
    {
        var wrapperType = ResolveManagedWrapperType(fullTypeName);
        return new
        {
            name = wrapperType.FullName,
            assembly = wrapperType.Assembly.GetName().Name,
            baseType = wrapperType.BaseType?.FullName,
            fields = wrapperType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(f => new { f.Name, type = f.FieldType.FullName })
                .ToArray(),
            properties = wrapperType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(p => new { p.Name, type = p.PropertyType.FullName, p.CanRead, p.CanWrite })
                .ToArray(),
        };
    }

    public static string SafeName(object? value)
    {
        if (value == null)
            return string.Empty;

        try
        {
            var nameProperty = value.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.Instance);
            if (nameProperty?.GetValue(value) is string name && !string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch
        {
        }

        try
        {
            return value.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            return $"<ToString error: {ex.Message}>";
        }
    }

    public static object? TryCast(object? value, string fullTypeName)
    {
        if (value == null)
            return null;

        var wrapperType = ResolveManagedWrapperType(fullTypeName);
        var method = typeof(Il2CppHelpers).GetMethod(nameof(TryCastGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(wrapperType);
        return method.Invoke(null, new[] { value });
    }

    private static object? TryCastGeneric<T>(object value) where T : class
    {
        var method = value.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "TryCast" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
        if (method == null)
            return value as T;
        return method.MakeGenericMethod(typeof(T)).Invoke(value, Array.Empty<object>());
    }

    private static Type ResolveManagedWrapperType(string fullTypeName)
    {
        var normalized = fullTypeName.StartsWith("Il2Cpp", StringComparison.Ordinal)
            ? fullTypeName
            : "Il2Cpp." + fullTypeName;

        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(asm => asm.GetType(normalized, throwOnError: false))
            .FirstOrDefault(t => t != null);

        if (type == null)
            throw new InvalidOperationException($"Could not resolve IL2CPP wrapper type '{normalized}'.");

        return type;
    }

    private static Il2CppSystem.Type CreateIl2CppType(Type wrapperType)
    {
        var method = typeof(Il2CppType).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Il2CppType.Of) && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
        return (Il2CppSystem.Type)method.MakeGenericMethod(wrapperType).Invoke(null, Array.Empty<object>())!;
    }
}
```

If this does not compile against the installed `Il2CppInterop.Runtime`, inspect the actual API and make the smallest adjustment while preserving the public helper surface: `FindObjects`, `DescribeType`, `SafeName`, `TryCast`.

- [ ] **Step 3: Inject IL2CPP helpers in MelonLoader host**

Modify `src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj`:

```xml
<ProjectReference Include="../HotRepl.Helpers.Il2Cpp/HotRepl.Helpers.Il2Cpp.csproj" />
```

Modify `src/HotRepl.Host.MelonLoader/MelonLoaderHost.cs`:

```csharp
using HotRepl.Helpers.Il2Cpp;
```

Change signatures:

```csharp
private static readonly string[] _helperSignatures =
    HelperInjector.BuildSignatures(typeof(UnityHelpers))
        .Concat(HelperInjector.BuildSignatures(typeof(Il2CppHelpers)))
        .ToArray();
```

Change assemblies:

```csharp
private static readonly IReadOnlyList<Assembly> _additionalAssemblies =
    new[] { typeof(UnityHelpers).Assembly, typeof(Il2CppHelpers).Assembly };
```

Change usings:

```csharp
private static readonly IReadOnlyList<string> _additionalUsings =
    new[] { "HotRepl.Helpers.Unity", "HotRepl.Helpers.Il2Cpp", "Il2CppInterop.Runtime" };
```

Return `_helperSignatures` from `AdditionalHelperSignatures`.

- [ ] **Step 4: Add project to solution**

Modify `HotRepl.slnx` under `/src/`:

```xml
<Project Path="src/HotRepl.Helpers.Il2Cpp/HotRepl.Helpers.Il2Cpp.csproj" />
```

- [ ] **Step 5: Build with local MelonLoader paths**

```bash
dotnet build src/HotRepl.Helpers.Il2Cpp/HotRepl.Helpers.Il2Cpp.csproj --nologo -v q -p:MelonLoaderPath="/Users/joaichberger/Library/Application Support/CrossOver/Bottles/Steam/drive_c/Program Files (x86)/Steam/steamapps/common/Ancient Kingdoms/MelonLoader" -p:Il2CppAssembliesPath="/Users/joaichberger/Library/Application Support/CrossOver/Bottles/Steam/drive_c/Program Files (x86)/Steam/steamapps/common/Ancient Kingdoms/MelonLoader/Il2CppAssemblies"

dotnet build src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj --nologo -v q -p:MelonLoaderPath="/Users/joaichberger/Library/Application Support/CrossOver/Bottles/Steam/drive_c/Program Files (x86)/Steam/steamapps/common/Ancient Kingdoms/MelonLoader" -p:Il2CppAssembliesPath="/Users/joaichberger/Library/Application Support/CrossOver/Bottles/Steam/drive_c/Program Files (x86)/Steam/steamapps/common/Ancient Kingdoms/MelonLoader/Il2CppAssemblies"
```

Expected: PASS. If it fails, update only the API mismatch shown by the compiler and rerun.

- [ ] **Step 6: Document smoke evals**

Add to `README.md` MelonLoader section:

```markdown
After deploying to an IL2CPP game, verify:

```bash
hotrepl info
hotrepl eval '1 + 1'
hotrepl eval 'UnityEngine.Application.version'
hotrepl eval 'Il2CppHelpers.DescribeType("Il2Cpp.Monster")'    # use a type present in the target game
hotrepl eval 'Il2CppHelpers.FindObjects("Il2Cpp.Monster").Length'
```

Use a game-local wrapper type for the last two commands; HotRepl itself remains
game-agnostic.
```

- [ ] **Step 7: Commit**

```bash
git add HotRepl.slnx src/HotRepl.Helpers.Il2Cpp src/HotRepl.Host.MelonLoader README.md
git commit -m "feat(il2cpp): add helper assembly for runtime wrappers"
```

Commit body:

```text
Runtime probes need a game-agnostic way to work with IL2CPP wrapper types.
The helper assembly exposes reflection-based lookup and safe inspection
without embedding any target game's domain concepts into HotRepl.
```

---

### Task 8: Add Roslyn isolated evaluator under net6

**Files:**
- Create: `src/HotRepl.Evaluator.Roslyn/RoslynIsolatedEvaluator.cs`
- Create: `tests/HotRepl.Tests/Unit/RoslynIsolatedEvaluatorTests.cs`
- Modify: `src/HotRepl.Evaluator.Roslyn/RoslynEvaluatorFactory.cs`
- Modify: `src/HotRepl.Evaluator.Roslyn/HotRepl.Evaluator.Roslyn.csproj`

- [ ] **Step 1: Write failing isolated evaluator tests**

Create `tests/HotRepl.Tests/Unit/RoslynIsolatedEvaluatorTests.cs`:

```csharp
#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HotRepl.Evaluator;
using HotRepl.Evaluator.Roslyn;
using Xunit;

namespace HotRepl.Tests.Unit;

public class RoslynIsolatedEvaluatorTests
{
    private sealed class TestHost : IReplHost
    {
        public ReplConfig Config { get; } = new();
        public HostInfo HostInfo { get; } = new() { Name = "Tests", Version = "1", Runtime = ".NET", Platform = "Unit" };
        public IReadOnlyList<EvaluatorCapabilities> AvailableEvaluators => RoslynEvaluatorFactory.Capabilities;
        public string DefaultEvaluatorName => RoslynIsolatedEvaluator.IsolatedCapabilities.Name;
        public ICodeEvaluator CreateEvaluator(string evaluatorName) => RoslynEvaluatorFactory.Create(evaluatorName, this);
        public IReadOnlyList<Assembly> AdditionalAssemblies { get; } = Array.Empty<Assembly>();
        public IReadOnlyList<string> AdditionalUsings { get; } = Array.Empty<string>();
        public string[] AdditionalHelperSignatures { get; } = Array.Empty<string>();
        public void LogInfo(string message) { }
        public void LogDebug(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message, Exception? ex = null) { }
    }

    [Fact]
    public void Capabilities_AreIsolatedAndNonPersistent()
    {
        Assert.Equal("Roslyn.Isolated", RoslynIsolatedEvaluator.IsolatedCapabilities.Name);
        Assert.False(RoslynIsolatedEvaluator.IsolatedCapabilities.SupportsPersistentState);
        Assert.Equal(TimeoutMode.Cooperative, RoslynIsolatedEvaluator.IsolatedCapabilities.TimeoutMode);
    }

    [Fact]
    public void Evaluate_DoesNotPreserveVariablesAcrossRuns()
    {
        using var evaluator = new RoslynIsolatedEvaluator(new TestHost());
        evaluator.Initialize();

        Assert.True(evaluator.Evaluate("var x = 1;", CancellationToken.None).Success);
        var result = evaluator.Evaluate("x", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("compile", result.ErrorKind);
    }

    [Fact]
    public void Evaluate_ReturnsValue()
    {
        using var evaluator = new RoslynIsolatedEvaluator(new TestHost());
        evaluator.Initialize();

        var result = evaluator.Evaluate("return 21 * 2;", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(42, result.Value);
    }
}
#endif
```

Run:

```bash
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q --filter RoslynIsolatedEvaluatorTests
```

Expected: FAIL because `RoslynIsolatedEvaluator` does not exist or tests are not compiled for `net6.0`. If the test project only targets `net10.0`, the `NET6_0_OR_GREATER` symbol is true and the tests should compile once the type exists.

- [ ] **Step 2: Implement isolated evaluator**

Create `src/HotRepl.Evaluator.Roslyn/RoslynIsolatedEvaluator.cs`:

```csharp
#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using HotRepl.Evaluator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HotRepl.Evaluator.Roslyn;

public sealed class RoslynIsolatedEvaluator : ICodeEvaluator
{
    public static readonly EvaluatorCapabilities IsolatedCapabilities = new()
    {
        Name = "Roslyn.Isolated",
        LanguageVersion = "latest",
        SupportsPersistentState = false,
        SupportsCompletion = false,
        TimeoutMode = TimeoutMode.Cooperative,
    };

    private readonly IReplHost _host;
    private readonly List<Assembly> _referencedAssemblies = new();
    private readonly HashSet<string> _imports = new(StringComparer.Ordinal);
    private bool _isInitialized;

    public RoslynIsolatedEvaluator(IReplHost host) => _host = host;

    public bool IsInitialized => _isInitialized;
    public EvaluatorCapabilities Capabilities => IsolatedCapabilities;
    public bool PendingHotReload => false;
    public string? PendingHotReloadAssembly => null;

    public void Initialize() => _isInitialized = true;

    public EvalOutcome Evaluate(string code, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var previousOut = Console.Out;
        using var capture = new StringWriter();
        Console.SetOut(capture);

        try
        {
            var result = CompileAndRun(code, cancellationToken);
            sw.Stop();
            return result != null
                ? EvalOutcome.Ok(result, result.GetType().FullName, Stdout(capture), sw.ElapsedMilliseconds)
                : EvalOutcome.OkVoid(Stdout(capture), sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return EvalOutcome.Aborted;
        }
        catch (CompilationFailedException ex)
        {
            sw.Stop();
            return EvalOutcome.CompileError(ex.Message, Stdout(capture), sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return EvalOutcome.RuntimeError(ex.Message, ex.StackTrace, Stdout(capture), sw.ElapsedMilliseconds);
        }
        finally
        {
            Console.SetOut(previousOut);
        }
    }

    public CompletionResult Complete(string code, int cursorPos) => new(Array.Empty<string>(), 0);
    public void Reset()
    {
        _referencedAssemblies.Clear();
        _imports.Clear();
        _isInitialized = true;
    }
    public void ReferenceAssembly(Assembly assembly)
    {
        if (!assembly.IsDynamic && !string.IsNullOrEmpty(SafeLocation(assembly)))
            _referencedAssemblies.Add(assembly);
    }
    public void RunInternal(string code)
    {
        var trimmed = code.Trim();
        if (trimmed.StartsWith("using ", StringComparison.Ordinal) && trimmed.EndsWith(";", StringComparison.Ordinal))
            _imports.Add(trimmed.Substring("using ".Length, trimmed.Length - "using ".Length - 1).Trim());
    }
    public void Dispose() { }

    private object? CompileAndRun(string code, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var imports = new[] { "System", "System.Linq" }
            .Concat(_host.AdditionalUsings)
            .Concat(_imports)
            .Distinct()
            .Select(ns => $"using {ns};");
        var source = string.Join("\n", imports)
            + "\npublic static class __HotReplSnippet { public static object? Run() { "
            + code
            + "\nreturn null; } }";
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Concat(_host.AdditionalAssemblies)
            .Concat(_referencedAssemblies)
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(SafeLocation(a)))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Distinct()
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "HotRepl.Isolated.Snippet",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var pe = new MemoryStream();
        var emit = compilation.Emit(pe, cancellationToken: cancellationToken);
        if (!emit.Success)
            throw new CompilationFailedException(string.Join(Environment.NewLine, emit.Diagnostics));

        pe.Position = 0;
        var alc = new AssemblyLoadContext("HotRepl.Isolated", isCollectible: true);
        WeakReference? weak = null;
        try
        {
            var asm = alc.LoadFromStream(pe);
            weak = new WeakReference(asm, trackResurrection: false);
            var type = asm.GetType("__HotReplSnippet", throwOnError: true)!;
            var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            return method.Invoke(null, Array.Empty<object>());
        }
        finally
        {
            alc.Unload();
            _ = weak;
        }
    }

    private static string? SafeLocation(Assembly assembly)
    {
        try { return assembly.Location; }
        catch { return null; }
    }

    private static string? Stdout(StringWriter writer)
    {
        var value = writer.ToString();
        return value.Length > 0 ? value : null;
    }

    private sealed class CompilationFailedException : Exception
    {
        public CompilationFailedException(string message) : base(message) { }
    }
}
#endif
```

- [ ] **Step 3: Register isolated evaluator in factory under net6**

Modify `src/HotRepl.Evaluator.Roslyn/RoslynEvaluatorFactory.cs`:

```csharp
private static readonly EvaluatorCapabilities[] _capabilities =
{
    RoslynScriptEvaluator.ScriptCapabilities,
#if NET6_0_OR_GREATER
    RoslynIsolatedEvaluator.IsolatedCapabilities,
#endif
};
```

Extend create:

```csharp
#if NET6_0_OR_GREATER
if (evaluatorName == RoslynIsolatedEvaluator.IsolatedCapabilities.Name)
    return new RoslynIsolatedEvaluator(host);
#endif
```

- [ ] **Step 4: Run isolated tests and full unit suite**

```bash
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q --filter "RoslynIsolatedEvaluatorTests|RoslynScriptEvaluatorTests|SelectEvaluatorProtocolTests"
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q
```

Expected: PASS. If snippet syntax requires expressions without `return`, update tests and implementation together so the contract is explicit. Do not silently accept both statement and expression forms unless tests cover both.

- [ ] **Step 5: Commit**

```bash
git add src/HotRepl.Evaluator.Roslyn tests/HotRepl.Tests
git commit -m "feat(evaluator): add isolated Roslyn mode"
```

Commit body:

```text
Audit probes need a stateless evaluator for repeatable snippets that do not
inherit prior REPL variables. The isolated Roslyn mode gives clients that
boundary on modern runtimes while keeping Roslyn.Script as the persistent default.
```

---

### Task 9: Update client smoke tests and documentation

**Files:**
- Create: `client/tests/test_select_evaluator.py`
- Modify: `client/tests/test_handshake.py`
- Modify: `client/tests/test_eval_errors.py`
- Modify: `README.md`
- Modify: `AGENTS.md`
- Modify: `.claude/skills/hotrepl/SKILL.md`

- [ ] **Step 1: Make handshake smoke tests accept metadata**

Modify `client/tests/test_handshake.py`:

```python
async def test_handshake_metadata_when_present(client: Client) -> None:
    assert client.handshake is not None
    evaluator = client.handshake.get("evaluator")
    if evaluator is not None:
        assert isinstance(evaluator.get("name"), str)
        assert isinstance(evaluator.get("languageVersion"), str)
        assert evaluator.get("timeoutMode") in {"HardAbort", "Cooperative", "None"}

    host = client.handshake.get("host")
    if host is not None:
        assert isinstance(host.get("name"), str)
        assert isinstance(host.get("runtime"), str)

    available = client.handshake.get("availableEvaluators")
    if available is not None:
        assert isinstance(available, list)
        assert all(isinstance(name, str) for name in available)
```

Keep existing tests for backward compatibility.

- [ ] **Step 2: Add evaluator selection smoke tests**

Create `client/tests/test_select_evaluator.py`:

```python
from __future__ import annotations

import pytest

from hotrepl import Client, EvalError

pytestmark = pytest.mark.asyncio


async def test_select_current_evaluator(client: Client) -> None:
    assert client.handshake is not None
    evaluator = client.handshake.get("evaluator") or {}
    current = evaluator.get("name")
    if not isinstance(current, str) or not current:
        pytest.skip("server does not advertise evaluator metadata")

    result = await client.select_evaluator(current)

    assert result["type"] == "select_evaluator_result"
    assert result["success"] is True
    assert result["evaluator"] == current


async def test_select_unknown_evaluator_reports_unsupported(client: Client) -> None:
    with pytest.raises(EvalError) as exc_info:
        await client.select_evaluator("Missing.Evaluator")

    assert exc_info.value.kind == "unsupported"
```

- [ ] **Step 3: Fix legacy error-kind expectations if current tests still expect old names**

If `client/tests/test_eval_errors.py` expects `"compilation"`, update it to the server's documented `"compile"` wire value:

```python
assert exc_info.value.kind == "compile"
```

Do not change server errorKind to `"compilation"`; `README.md` and Core constants use `"compile"`.

- [ ] **Step 4: Update README protocol and limitations**

In `README.md` update the handshake JSON to include optional metadata:

```json
{
  "type": "handshake",
  "version": "1.0.0",
  "csharpVersion": "latest",
  "evaluator": {
    "name": "Roslyn.Script",
    "languageVersion": "latest",
    "supportsPersistentState": true,
    "supportsCompletion": false,
    "timeoutMode": "Cooperative"
  },
  "host": {
    "name": "MelonLoader",
    "version": "0.x",
    "runtime": ".NET 6",
    "platform": "Unity IL2CPP"
  },
  "availableEvaluators": ["Roslyn.Script", "Roslyn.Isolated"],
  "defaultUsings": ["System", "System.Linq", "HotRepl.Helpers.Unity"],
  "helpers": ["String[] Help()", "Object History(Int32 limit = 20)"]
}
```

Update `errorKind` list:

```markdown
- `errorKind`: `compile` | `runtime` | `timeout` | `cancelled` | `unsupported`
```

Replace the old `Mono JIT only` limitation with:

```markdown
| Limitation | Details |
|---|---|
| Timeout mode depends on evaluator | Mono.CSharp reports `HardAbort`; Roslyn reports `Cooperative`, so a runaway runtime loop can still require restarting the game |
| Completion depends on evaluator | Mono.CSharp supports completion; Roslyn evaluators currently report `supportsCompletion: false` |
| Type memory leak | Persistent evaluator sessions can emit assemblies that are not reclaimed until process exit; use `Roslyn.Isolated` for stateless audit snippets on .NET 6 hosts |
| Single client | A new WebSocket connection replaces the prior session; old subscriptions are cancelled |
```

- [ ] **Step 5: Update AGENTS and HotRepl skill**

In `AGENTS.md`, update architecture invariants:

```markdown
- **Evaluator timeout is capability-driven**: `TimeoutMode.HardAbort` may abort the
  main thread; `TimeoutMode.Cooperative` cancels a token and cannot preempt every
  runtime loop. Do not claim all evaluators have hard timeouts.
- **Core has no compiler stack**: Mono.CSharp and Roslyn live in evaluator projects.
  Core must not reference `mcs.dll`, Roslyn packages, UnityEngine, BepInEx,
  MelonLoader, or Il2CppInterop.
```

In `.claude/skills/hotrepl/SKILL.md`, update the top description and connection guidance so agents know to run:

```bash
hotrepl info
hotrepl select-evaluator Roslyn.Isolated
```

when an IL2CPP audit needs stateless snippets.

- [ ] **Step 6: Run Python checks**

```bash
cd client
uv run ruff check src/ tests/
uv run mypy src/hotrepl/
uv run pytest tests/ -v --tb=short
```

Expected: ruff PASS, mypy PASS, pytest either PASS against a running server or SKIP connection-dependent smoke tests when no server is reachable.

- [ ] **Step 7: Run C# checks**

```bash
dotnet build src/HotRepl.Core/HotRepl.Core.csproj --nologo -v q
dotnet build src/HotRepl.Evaluator.MonoCSharp/HotRepl.Evaluator.MonoCSharp.csproj --nologo -v q
dotnet build src/HotRepl.Evaluator.Roslyn/HotRepl.Evaluator.Roslyn.csproj --nologo -v q
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q
dotnet format src/HotRepl.Core/HotRepl.Core.csproj --verify-no-changes --no-restore
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add README.md AGENTS.md .claude/skills/hotrepl/SKILL.md client tests
git commit -m "docs(protocol): document host and evaluator selection"
```

Commit body:

```text
The client and agent docs need to expose evaluator capabilities because
runtime behavior now differs by compiler and host. Documenting timeout and
selection semantics prevents callers from assuming Mono.CSharp behavior on IL2CPP.
```

---

### Task 10: Runtime validation checkpoints

**Files:**
- Modify only documentation if validation reveals documented commands are wrong.
- Do not add Ancient Kingdoms-specific code to HotRepl.

- [ ] **Step 1: BepInEx/Mono smoke validation**

Deploy the built BepInEx bundle to an existing BepInEx/Mono game used for HotRepl validation. Use the repo's existing deploy convention; if none exists, copy the built adapter DLLs and `mcs.dll` to `BepInEx/plugins/HotRepl/` side-by-side.

Run from `client/`:

```bash
uv run hotrepl info
uv run hotrepl ping
uv run hotrepl eval '1 + 1'
uv run hotrepl eval 'var x = 40;'
uv run hotrepl eval 'x + 2'
uv run hotrepl complete 'Console.'
uv run hotrepl test -v
```

Expected:

- `info` reports host `BepInEx` and default evaluator `Mono.CSharp`.
- `ping` returns a latency line.
- `1 + 1` prints `2`.
- Persistent state returns `42`.
- Completion returns non-empty candidates for `Console.`.
- Smoke suite passes or skips only tests that require unsupported runtime conditions.

- [ ] **Step 2: BepInEx Roslyn smoke validation**

With the same running BepInEx game:

```bash
uv run hotrepl select-evaluator Roslyn.Script
uv run hotrepl info
uv run hotrepl eval '1 + 1'
uv run hotrepl eval 'var y = 100;'
uv run hotrepl eval 'y + 23'
uv run hotrepl eval 'while (true) { }' --timeout 500
```

Expected:

- Selection succeeds only if Roslyn dependencies deploy correctly.
- `info` reports evaluator `Roslyn.Script` and timeout `Cooperative`.
- Persistent state returns `123`.
- Tight loop may not recover under cooperative timeout; if it hangs, restart the game and record this as expected documented behavior, not a test failure.

- [ ] **Step 3: MelonLoader/IL2CPP smoke validation against Ancient Kingdoms**

Build with the local Ancient Kingdoms paths and deploy side-by-side DLLs to `Mods/` or the validated MelonLoader dependency directory. Do not hard-code these paths in committed files.

Run:

```bash
uv run hotrepl info
uv run hotrepl ping
uv run hotrepl eval '1 + 1'
uv run hotrepl eval 'UnityEngine.Application.version'
uv run hotrepl eval 'using Il2Cpp; using Il2CppInterop.Runtime; Il2CppType.Of<Il2Cpp.Monster>() != null'
uv run hotrepl eval 'Il2CppHelpers.FindObjects("Il2Cpp.Monster").Length'
uv run hotrepl eval 'UnityHelpers.SceneGraph(null, null, 1, 5)'
```

Expected:

- `info` reports host `MelonLoader` and evaluator `Roslyn.Script`.
- `1 + 1` returns `2`.
- Application version returns a non-empty value.
- `Il2CppType.Of<Il2Cpp.Monster>() != null` returns `true`.
- Monster lookup returns a number greater than zero in a loaded game state where monster assets are available.
- Scene graph returns an array-like serialized value.

If Ancient Kingdoms is not available, use another local MelonLoader IL2CPP game and replace `Il2Cpp.Monster` with a wrapper type known to exist in that game. Label the result as equivalent IL2CPP validation, not Ancient Kingdoms validation.

- [ ] **Step 4: Document validation corrections**

If any command or deploy layout in `README.md`, `AGENTS.md`, or `.claude/skills/hotrepl/SKILL.md` was wrong, edit those docs to match observed behavior and run:

```bash
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q
cd client && uv run ruff check src/ tests/ && uv run mypy src/hotrepl/
```

Expected: PASS.

- [ ] **Step 5: Commit validation docs if needed**

If docs changed:

```bash
git add README.md AGENTS.md .claude/skills/hotrepl/SKILL.md
git commit -m "docs(validation): record host smoke-test workflow"
```

If no docs changed, do not create an empty commit.

---

## Final verification before handing back

Run from `/Users/joaichberger/Projects/HotRepl`:

```bash
dotnet build src/HotRepl.Core/HotRepl.Core.csproj --nologo -v q
dotnet build src/HotRepl.Evaluator.MonoCSharp/HotRepl.Evaluator.MonoCSharp.csproj --nologo -v q
dotnet build src/HotRepl.Evaluator.Roslyn/HotRepl.Evaluator.Roslyn.csproj --nologo -v q
dotnet test tests/HotRepl.Tests/HotRepl.Tests.csproj --nologo -v q
dotnet format src/HotRepl.Core/HotRepl.Core.csproj --verify-no-changes --no-restore
```

Run from `/Users/joaichberger/Projects/HotRepl/client`:

```bash
uv run ruff check src/ tests/
uv run mypy src/hotrepl/
uv run pytest tests/ -v --tb=short
```

Run game-host validations when the corresponding games are available:

```bash
uv run hotrepl info
uv run hotrepl ping
uv run hotrepl test -v
```

For MelonLoader/IL2CPP, also run:

```bash
uv run hotrepl eval 'UnityEngine.Application.version'
uv run hotrepl eval 'using Il2Cpp; using Il2CppInterop.Runtime; Il2CppType.Of<Il2Cpp.Monster>() != null'
uv run hotrepl eval 'Il2CppHelpers.FindObjects("Il2Cpp.Monster").Length'
```

A completion claim requires observed passing output for the non-game checks and explicit notes for which runtime smoke validations were run, skipped, or blocked by missing local game installs.
