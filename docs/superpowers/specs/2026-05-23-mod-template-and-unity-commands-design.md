# Mod template + UnityCommands plugin design

**Status:** Draft for review — v0.3, incorporates self-review (§12) and external annotation feedback
on schema generation.\
(§12).\
**Scope:** One spec covering five touched code targets. The decomposition into phased implementation
work happens in the follow-up plan file.

---

## 1. Goal & non-goals

### 1.1 Goal

Close the largest remaining hole in HotRepl's user / developer / agent experience: HotRepl has a
great runtime and a great network surface, but **zero shipped command surface out of the box** and
**zero scaffold for new plugin authors**. Every consumer reinvents the same patterns and learns the
typed-command API by reading Ardenfall's source.

Three deliverables, sized to "demonstration + maintenance burden low", not "comprehensive Unity
runtime API":

1. **A new strongly-typed command interface in `HotRepl.Core`**
   (`IControlCommandHandler<TArgs, TResult>`) that derives its protocol-level JSON schemas from the
   C# arg/result POCO types via NJsonSchema 10.9.0 (Newtonsoft-only, no System.Text.Json — see
   §2.3). Removes the "hand-write a `JObject` schema literal next to every command, hope it matches
   your deserialization" footgun that every existing consumer has hit.

2. **A first-party `HotRepl.UnityCommands` plugin** that ships 4 commands — one per architectural
   pattern (no-args sync, typed-args sync, destructive-mutating, artifact-producing) — so a new
   contributor has a working reference to copy from, and every HotRepl install has a non-empty
   `tools/list` out of the box. Ships for both BepInEx/Mono and MelonLoader/IL2CPP.

3. **A `glockyco/hotrepl-mod-template` GitHub template repo** containing a minimal working mod
   (BepInEx + MelonLoader sibling projects, one demo command, build/deploy scripts) plus an embedded
   `dotnet new` manifest. Click "Use this template" or `dotnet new install <path>` and have a
   compiling, deployable mod in under a minute.

Plus three coordinated migrations of existing consumers, so each of them exits this work as a
"best-practice example of how to build a HotRepl consumer" instead of a snapshot of an evolving
pattern:

4. **Ardenfall** — 1:1 migration of all existing typed commands to the new generic interface. Drop
   the hand-written `CompendiumCommandSchemas`.
5. **Ancient Kingdoms** — delete the `AutoExporter` mod and the `.exporter-result.json` round-trip
   entirely; replace with a small new typed-command mod (`HotReplCommands`) that owns world-entry
   automation, export, and screenshots as proper job-pattern commands. Rewrite `build-tool export`
   to drive over the HotRepl WebSocket.
6. **Erenshor** — replace the custom Fleck WebSocket server in the `MapTileCapture` mod with typed
   HotRepl commands. The Python pipeline that currently drives that custom protocol shells out to
   `bunx
   @hotrepl/cli run` instead.

### 1.2 Non-goals

Explicitly **NOT** in scope for this work; each could plausibly come later behind an issue.

| Out of scope                                                                                                                          | Why                                                                                                                                                                                                              |
| ------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Tier 2/3 commands in UnityCommands (scene tree, component dump, transform set, asset find-by-name, input simulation, recording, etc.) | Maintenance burden on every Unity version + IL2CPP runtime change. Agents will use `eval` for these anyway — it's more flexible and matches their training data. Re-evaluate if real demand surfaces via issues. |
| Hot config reload (toggle commands at runtime)                                                                                        | Mid-flight `GlobalControlCommandRegistry` mutation while an agent is mid-command is genuinely hard to get right. Restart-required is fine for a setting flipped maybe once per project lifetime.                 |
| Publishing the template to NuGet (`dotnet new install hotrepl-mod`)                                                                   | The GitHub-template-button path covers the discoverability win; NuGet publish is an extra release surface without proportional UX gain. Trivial to add later.                                                    |
| Publishing UnityCommands to Thunderstore                                                                                              | Thunderstore is the standard channel for game-mod consumers, not developer tooling. Wrong audience.                                                                                                              |
| Migrating Erenshor's `InteractiveMapCompanion` mod                                                                                    | Per maintainer decision. It's a live-state streaming mod, not an export mod.                                                                                                                                     |
| Migrating AK's other 11 mods (BossMod, BossTracker, MapEnhancer, …)                                                                   | None of them have an obvious typed-command shape — they're event-driven gameplay tweaks. Forcing commands onto them creates fake examples.                                                                       |
| A second non-Unity demo plugin (`HotRepl.GenericCommands`?)                                                                           | Premature. Demonstrate one well, expand if a non-Unity consumer ever materializes.                                                                                                                               |
| `IControlCommandHandler<TArgs, TResult, TArtifacts>` for typed artifact maps                                                          | The artifact map is intrinsically open (any number of named artifacts per result). `JObject`-via-existing-channel stays the right shape.                                                                         |
| Per-command rate limiting / permission gates                                                                                          | Loopback + single-client is the authority model; per-command policy is YAGNI.                                                                                                                                    |

---

## 2. Architecture

### 2.1 New strongly-typed command interface (`HotRepl.Core`)

Single additive change. The existing `IControlCommandHandler` (non-generic, `JObject`-based) stays
exactly as-is — every Ardenfall handler keeps compiling. The new generic interface lives alongside
it.

```csharp
// HotRepl.Core — additive, no breaking change.
public interface IControlCommandHandler<TArgs, TResult> {
    string             Name         { get; }
    int                MajorVersion { get; }
    ControlCommandKind Kind         { get; }
    bool               MutatesState { get; }

    ValueTask<TResult> ExecuteAsync(
        ControlCommandContext context,
        TArgs args,
        CancellationToken cancellationToken);
}
```

Properties match the descriptor fields the existing dispatcher already consumes; the args/result
schemas are derived (see §2.3). `TArgs == EmptyArgs` is the canonical "no args" form.

```csharp
public readonly struct EmptyArgs { }
```

`EmptyArgs` is a zero-field struct in `HotRepl.Core`. The schema generator (see §2.3) treats it as a
special case and emits `{ "type": "object", "additionalProperties": false }` — the cleanest possible
schema for "this command takes no arguments."

**Runtime-specific commands** (e.g., a hypothetical command that requires Mono-Unity reflection that
doesn't compile on IL2CPP-unhollowed types) are handled at **build time** via shared-source
compilation (§2.5), not via a runtime capability flag. There is no `SupportedRuntime` property on
the interface. Each loader's csproj statically selects which command source files it compiles;
commands that don't compile for a runtime simply aren't present there. This is simpler, avoids C# 8
default-interface-method requirements (which pre-2021.2 Unity Mono doesn't support), and removes a
class of "wait, why does this command appear in tools/list but always return an error?" bugs.

### 2.2 Generic-to-non-generic adapter

The dispatcher continues to consume `IControlCommandHandler` only. A single adapter class in
`HotRepl.Core` bridges the two interfaces. Concrete game-specific handlers never see the adapter;
they only implement the generic interface.

```csharp
// HotRepl.Core — internal class, exposed only through Register<TArgs,TResult>.
internal sealed class TypedControlCommandAdapter<TArgs, TResult>
    : IControlCommandHandler
{
    private readonly IControlCommandHandler<TArgs, TResult> _inner;
    private readonly JsonSerializerSettings _settings;

    public ControlCommandDescriptor Descriptor { get; }

    public TypedControlCommandAdapter(
        IControlCommandHandler<TArgs, TResult> inner,
        JsonSerializerSettings settings)
    {
        _inner    = inner;
        _settings = settings;
        Descriptor = new ControlCommandDescriptor(
            name:            inner.Name,
            majorVersion:    inner.MajorVersion,
            kind:            inner.Kind,
            mutatesState:    inner.MutatesState,
            argsSchema:      SchemaCache.For<TArgs>(),
            resultSchema:    SchemaCache.For<TResult>(),
            artifactsSchema: SchemaCache.AnyObject);
    }

    public async ValueTask<ControlCommandResult> ExecuteAsync(
        ControlCommandContext context, JObject args, CancellationToken ct)
    {
        TArgs typedArgs = typeof(TArgs) == typeof(EmptyArgs)
            ? default!
            : (args.ToObject<TArgs>(JsonSerializer.Create(_settings))
                ?? throw new ArgumentException(
                    $"Failed to deserialize args of type {typeof(TArgs).Name}."));

        TResult typedResult = await _inner
            .ExecuteAsync(context, typedArgs, ct)
            .ConfigureAwait(false);

        JObject output = typedResult is null
            ? new JObject()
            : JObject.FromObject(typedResult, JsonSerializer.Create(_settings));

        return new ControlCommandResult(
            output:    output,
            artifacts: Array.Empty<HotRepl.Control.Artifacts.ArtifactRef>(),
            errors:    Array.Empty<ControlCommandError>());
    }
}
```

A `IControlCommandRegistry.Register<TArgs, TResult>` extension method provides the ergonomic
registration site:

```csharp
public static class ControlCommandRegistryExtensions {
    public static IDisposable Register<TArgs, TResult>(
        this IControlCommandRegistry registry,
        IControlCommandHandler<TArgs, TResult> handler)
        => registry.Register(
            new TypedControlCommandAdapter<TArgs, TResult>(
                handler,
                ProtocolJsonSerializerSettings.Instance));
}
```

A concrete handler is now:

```csharp
public sealed class UnityAppInfoCommand
    : IControlCommandHandler<EmptyArgs, UnityAppInfo>
{
    public string             Name         => "unity.app.info";
    public int                MajorVersion => 1;
    public ControlCommandKind Kind         => ControlCommandKind.Synchronous;
    public bool               MutatesState => false;

    public ValueTask<UnityAppInfo> ExecuteAsync(
        ControlCommandContext _, EmptyArgs __, CancellationToken ___)
        => new(new UnityAppInfo {
            ProductName  = UnityEngine.Application.productName,
            UnityVersion = UnityEngine.Application.unityVersion,
            Platform     = UnityEngine.Application.platform.ToString(),
        });
}
```

### 2.3 Schema generation

Schemas are generated by **NJsonSchema, pinned to `10.9.0`** — the last release before the
`System.Text.Json` transitive dependency was introduced in NJsonSchema 11.0. The 10.x line uses only
`Newtonsoft.Json` and `Namotion.Reflection`, both pure .NET and known-good on Mono Unity.

**Why NJsonSchema 10.9.0 specifically, not hand-rolled (v0.2) or NJsonSchema 11.x (v0.1):**

| Approach                                       | Problem                                                                                                                                                                                                                                                           |
| ---------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| NJsonSchema 11.x with side-by-side DLLs        | Transitive `System.Text.Json` dependency. STJ is documented-broken on Mono Unity (assembly-load + JIT issues across plugins).                                                                                                                                     |
| NJsonSchema 11.x with ILRepack internalization | ILRepack hides the assembly-resolution problem but cannot fix STJ's runtime-compatibility problem on Mono Unity. If STJ's JIT path fails inside `HotRepl.Core.dll`, internalization just changes where the crash originates.                                      |
| Hand-rolled minimal generator                  | Scope creep: JSON Schema has rich semantics and "I only need a subset" predictably turns into "I need just one more edge case." Pulls HotRepl's focus away from runtime REPL into schema-library territory.                                                       |
| Newtonsoft.Json.Schema (Newton King)           | Dual-licensed commercial + AGPL. AGPL is incompatible with HotRepl's MIT license. Hard block.                                                                                                                                                                     |
| **NJsonSchema 10.9.0**                         | Newtonsoft-only, no STJ anywhere in the dependency tree. Industry-standard library with attribute-driven schema generation. "Legacy" only in the sense that the author moved to 11.x for STJ users — the 10.x API surface we need is feature-complete and stable. |

10.9.0 is the right tool for our environment. The "what if 10.x ever needs a bug fix" risk is
bounded — Rico Suter is responsive to upstream PRs, the source is MIT-licensed and forkable, and
worst-case escape is a small build-time source generator using 11.x on CoreCLR + emit precomputed
schema strings into compile output (no runtime dependency at all).

#### Runtime dependency footprint

After this change `HotRepl.Core.dll` runtime-depends on:

- `Newtonsoft.Json` (already shipped today)
- `NJsonSchema` 10.9.0 (~500 KB, new)
- `Namotion.Reflection` (~150 KB, new — small reflection helper, NJsonSchema's only non-Newtonsoft
  dep)

To keep the consumer story unchanged (`HotRepl.Core.dll` plus `Newtonsoft.Json` are the only files a
downstream mod has to reference), NJsonSchema and Namotion.Reflection are **ILRepack-internalized
into `HotRepl.Core.dll`** at build time. Consumers see exactly one DLL — internally it carries the
schema engine.

This is a different use of ILRepack than the one HotRepl's `AGENTS.md` prohibits: that rule is about
not internalizing **consumer-facing** assemblies (Core itself, Newtonsoft, Fleck — things consumers
reference directly, where merging would break type identity). NJsonSchema and Namotion.Reflection
are pure implementation details — no consumer ever writes `using NJsonSchema;`. Internalizing them
is the textbook use case for ILRepack and is consistent with the spirit of the existing rule.

#### Public schema API

Small, stable surface in `HotRepl.Core`. Consumers never see NJsonSchema types.

```csharp
// HotRepl.Core/Schema/SchemaCache.cs — public.
public static class SchemaCache {
    private static readonly ConcurrentDictionary<Type, JObject> _cache = new();

    /// <summary>Empty-object schema with <c>additionalProperties: true</c>;
    /// universal artifacts-schema fallback.</summary>
    public static JObject AnyObject { get; } = JObject.Parse(
        "{ \"type\": \"object\", \"additionalProperties\": true }");

    /// <summary>EmptyArgs schema: empty object closed to extra properties.</summary>
    public static JObject EmptyObject { get; } = JObject.Parse(
        "{ \"type\": \"object\", \"additionalProperties\": false }");

    public static JObject For<T>() => For(typeof(T));

    public static JObject For(Type t) =>
        _cache.GetOrAdd(t, static type => Build(type));

    private static JObject Build(Type type) {
        if (type == typeof(EmptyArgs)) return EmptyObject;
        var schema = NJsonSchema.JsonSchema.FromType(type, BuilderSettings);
        return JObject.Parse(schema.ToJson());
    }

    private static readonly NJsonSchema.Generation.JsonSchemaGeneratorSettings
        BuilderSettings = new() {
            SerializerSettings = ProtocolJsonSerializerSettings.Instance,
            DefaultReferenceTypeNullHandling =
                NJsonSchema.Generation.ReferenceTypeNullHandling.NotNull,
            AllowReferencesWithProperties = false, // inline; no $ref
        };
}
```

Pre-warmed at adapter construction (plugin Awake) — the first agent request never pays for type
reflection.

#### Attributes honored (via NJsonSchema's normal attribute discovery)

These are standard .NET attributes; NJsonSchema reads them out-of-the-box.

| Attribute                                              | Effect on schema                           |
| ------------------------------------------------------ | ------------------------------------------ |
| `[Required]` (`System.ComponentModel.DataAnnotations`) | Adds property to parent's `required` array |
| `[Range(min, max)]`                                    | Adds `minimum`/`maximum` to number schemas |
| `[Description("…")]` (`System.ComponentModel`)         | Adds to `description` field                |
| `[JsonProperty("wireName")]` (Newtonsoft)              | Renames the property in the schema         |
| `[JsonIgnore]` (Newtonsoft)                            | Omits the property from the schema         |
| `[StringLength(min, max)]`                             | Adds `minLength` / `maxLength` to strings  |

NJsonSchema covers many more attributes; the v1 demo commands and Ardenfall's migrated commands use
only the ones above.

### 2.4 Build pipeline and consumer-facing surface

```xml
<!-- HotRepl.Core.csproj -->
<ItemGroup>
  <PackageReference Include="NJsonSchema" Version="10.9.0"
                    PrivateAssets="all" GeneratePathProperty="true" />
  <PackageReference Include="ILRepack.Lib.MSBuild.Task" Version="2.*"
                    PrivateAssets="all" />
</ItemGroup>
```

```xml
<!-- ILRepack.targets — sibling of HotRepl.Core.csproj -->
<Target Name="ILRepack" AfterTargets="Build">
  <ItemGroup>
    <InputAssemblies Include="$(OutputPath)HotRepl.Core.dll" />
    <InputAssemblies Include="$(OutputPath)NJsonSchema.dll" />
    <InputAssemblies Include="$(OutputPath)Namotion.Reflection.dll" />
  </ItemGroup>
  <ItemGroup>
    <!-- Things deliberately NOT internalized — consumers reference them. -->
    <DoNotInternalize Include="HotRepl.Protocol" />
    <DoNotInternalize Include="Newtonsoft.Json" />
    <DoNotInternalize Include="UnityEngine" />
    <DoNotInternalize Include="BepInEx" />
  </ItemGroup>
  <ILRepack
    Parallel="true"
    Internalize="true"
    InternalizeExclude="@(DoNotInternalize)"
    InputAssemblies="@(InputAssemblies)"
    TargetKind="Dll"
    OutputFile="$(OutputPath)HotRepl.Core.dll" />
  <!-- Remove the merged inputs so they don't ship side-by-side. -->
  <Delete Files="$(OutputPath)NJsonSchema.dll;
                 $(OutputPath)Namotion.Reflection.dll" />
</Target>
```

After build, the consumer-facing dependency picture is **unchanged from today**:

- `HotRepl.Core.dll` ships (now ~3 MB instead of ~50 KB; same name and public API).
- `HotRepl.Protocol.dll` ships, unchanged.
- `Newtonsoft.Json.dll` ships, unchanged.
- `Fleck.dll` ships, unchanged.
- No new files in any consumer's plugin folder.
- No new compile-time references in any consumer csproj.
- No NJsonSchema visible from any consumer's code (it's internalized).

Consumer mod authors gain attribute-driven schema generation simply by using the new typed
`IControlCommandHandler<TArgs, TResult>` interface — they put `[Required]` / `[Range]` /
`[Description]` on POCO properties and the schemas come out correctly. They never have to think
about the schema engine underneath.

### 2.5 Plugin layout

**Three components, but only two csprojs.** Following the established dual-runtime Unity-mod pattern
(UnityExplorer, RuntimeUnityEditor, and many others), the command source files live in a **shared
source folder** with no .csproj of its own. Each loader-specific csproj `<Compile Include>`s the
shared source so the same code compiles against the Mono `UnityEngine.dll` in one project and the
IL2CPP-unhollowed `UnityEngine.dll` in the other.

```
src/HotRepl.UnityCommands/             ← shared source folder, NOT a csproj
  Commands/
    UnityAppInfoCommand.cs
    UnityGameObjectFindCommand.cs
    UnityTimeSetScaleCommand.cs
    UnityScreenshotCommand.cs
  Models/
    UnityAppInfo.cs
    UnityGameObjectFindArgs.cs
    UnityGameObjectFindResult.cs
    UnityGameObject.cs
    Vec3.cs
    UnitySetTimeScaleArgs.cs
    UnitySetTimeScaleResult.cs
    UnityScreenshotArgs.cs
    UnityScreenshotResult.cs
  UnityCommandCatalog.cs                ← static factory list, used by both loaders

src/HotRepl.UnityCommands.BepInEx/      ← netstandard2.1, references Mono UnityEngine
  Plugin.cs
  HotRepl.UnityCommands.BepInEx.csproj  ← <Compile Include="..\HotRepl.UnityCommands\**\*.cs" />

src/HotRepl.UnityCommands.MelonLoader/  ← net6.0, references unhollowed UnityEngine
  Mod.cs
  HotRepl.UnityCommands.MelonLoader.csproj  ← same <Compile Include>
```

The shared folder contains the actual command bodies. The two loader csprojs are tiny — they exist
purely to provide loader-specific `Plugin.cs` / `Mod.cs` entry points, the loader-specific
`[BepInPlugin]` / `[MelonInfo]` attributes, and the right `UnityEngine.dll` reference for the shared
source to compile against.

`HotRepl.UnityCommands.BepInEx` (the csproj):

- One `BaseUnityPlugin` subclass in `Plugin.cs`.
- `[BepInPlugin("hotrepl.unitycommands.bepinex", "HotRepl Unity Commands", VERSION)]`.
- `[BepInDependency("hotrepl.bepinex", BepInDependency.DependencyFlags.HardDependency)]` —
  guarantees BepInEx loads HotRepl first so `GlobalControlCommandRegistry.Instance` exists at our
  Awake.
- Two `Config.Bind` calls (see §2.6) and the per-loader registration loop iterating
  `UnityCommandCatalog`.
- `<Compile Include="..\HotRepl.UnityCommands\**\*.cs" Exclude="**\bin\**;**\obj\**" />` in the
  csproj.

`HotRepl.UnityCommands.MelonLoader` (the csproj):

- One `MelonMod` subclass in `Mod.cs`.
- `[MelonInfo(typeof(...), "HotRepl Unity Commands", VERSION, "glockyco")]`.
- MelonLoader doesn't have a portable `BepInDependency` equivalent. Two safe options for "register
  after HotRepl is ready":
  - **`OnLateInitializeMelon()`** — introduced in 0.6, called after every `OnInitializeMelon` across
    all mods. This is the canonical hook for "do something that requires other mods to have
    initialized first." We use this as the registration point. (Note: do NOT use
    `OnApplicationStart` — deprecated, removed from the public API in MelonLoader 0.7.0.)
  - **Defensive null-check fallback**: if `GlobalControlCommandRegistry.Instance` is still null at
    `OnLateInitializeMelon`, log a warning and abort registration cleanly. Don't crash the game.
- Two `MelonPreferences` entries (see §2.6).
- `<Compile Include="..\HotRepl.UnityCommands\**\*.cs" Exclude="**\bin\**;**\obj\**" />` identical
  to the BepInEx csproj.

**Future runtime-specific commands.** If a hypothetical future command can't compile against one
runtime (it needs Mono-only reflection over game types, say), the command source file moves OUT of
the shared folder and into the loader-specific subfolder where it can compile. The shared folder is
for commands that work everywhere; loader-specific folders are for the exceptions. For v1, all 4
commands are shared.

### 2.6 Configuration layer

Native per-loader config. The user-facing shape is identical between BepInEx and MelonLoader — same
section, same keys, same descriptions — so a project maintainer who has used one knows the other.

**BepInEx side (`BepInEx/config/hotrepl.unitycommands.bepinex.cfg`):**

```ini
[General]
## Master switch. When false, no UnityCommands handlers are registered.
## Changes apply on next game start.
Enabled = true

[Commands]
## Comma-separated command names to skip. Useful when a consumer's own mod
## registers a command with the same name and wants to win.
## Example: unity.time.set_scale, unity.screenshot.capture
Disabled =
```

**MelonLoader side (`UserData/MelonPreferences.cfg`, under category `HotRepl.UnityCommands`):**

```ini
[HotRepl.UnityCommands]
# Master switch. When false, no UnityCommands handlers are registered.
# Changes apply on next game start.
Enabled = true

# Comma-separated command names to skip. Useful when a consumer's own mod
# registers a command with the same name and wants to win.
Disabled = ""
```

Bound at Awake/`OnInitializeMelon`:

```csharp
// BepInEx Plugin.cs
var enabled = Config.Bind(
    "General", "Enabled", true,
    "Master switch. When false, no UnityCommands handlers are registered. " +
    "Changes apply on next game start.");

var disabled = Config.Bind(
    "Commands", "Disabled", "",
    "Comma-separated command names to skip. Useful when a consumer's own " +
    "mod registers a command with the same name and wants to win. " +
    "Example: unity.time.set_scale, unity.screenshot.capture");

if (!enabled.Value) {
    Logger.LogInfo("HotRepl.UnityCommands disabled via config. Skipping registration.");
    return;
}

var disabledNames = ParseCsv(disabled.Value);
foreach (var handler in UnityCommandCatalog.Build()) {
    if (disabledNames.Contains(handler.Descriptor.Name)) continue;
    _registrations.Add(GlobalControlCommandRegistry.Instance.Register(handler));
}
```

(MelonLoader path is structurally identical; only the config-binding API differs.)

**Hot-reload policy:** read at startup, period. The config description text explicitly says "Changes
apply on next game start." If a user genuinely needs runtime toggling, they can open an issue and
we'll design it with a real use case.

### 2.7 Build / distribution flow

`HotRepl.BepInEx.csproj` (the existing host) gains one project reference:

```xml
<ItemGroup>
  <ProjectReference Include="..\HotRepl.UnityCommands.BepInEx\HotRepl.UnityCommands.BepInEx.csproj" />
</ItemGroup>
```

Similarly for the MelonLoader host. MSBuild copies the UnityCommands assembly into the host's
`bin/Debug/netstandard2.1/` (or `net6.0/`) directory alongside everything else.

Every existing consumer deploy script reads from that directory and copies DLLs into the game's
plugin folder. None of them need code changes:

- Ardenfall: `bun run hotrepl:setup` → `bun run hotrepl:deploy` → `controller/src/deploy.ts` already
  does a "copy every DLL from the build output" pattern. UnityCommands shows up automatically.
- AK: `dotnet run --project build-tool deploy-host` → `HotReplDeployer.Deploy` has an explicit
  allowlist of file names. We update that allowlist in the AK migration (see §6) to include
  `HotRepl.UnityCommands.*.dll/pdb`.
- Erenshor: similar; whatever deploy mechanism is in use today (`scripts/deploy.sh` or equivalent)
  picks up the new DLL.

### 2.8 IL2CPP-specific notes

All 4 v1 commands use only Unity APIs that exist with identical signatures on both Mono and
IL2CPP-unhollowed runtimes (`Application.productName`, `Application.unityVersion`,
`GameObject.Find`, `Time.timeScale`, `ScreenCapture.CaptureScreenshotAsTexture`,
`ImageConversion.EncodeToPNG`). The source files compile identically against both UnityEngine.dll
variants referenced from each loader csproj. No `#if` directives, no per-loader forks of the command
body, no reflection over game-defined types.

The only divergence is at the **csproj reference level**:

- `HotRepl.UnityCommands.BepInEx.csproj` references the Mono-flavored `UnityEngine.dll` from the
  game's `Managed/` folder.
- `HotRepl.UnityCommands.MelonLoader.csproj` references the unhollowed `UnityEngine.dll` from the
  game's `MelonLoader/Il2CppAssemblies/` folder.

The shared source compiles against whichever one its compiling csproj is referencing. Same `.cs`
file, two compiled outputs, neither needs to know which runtime it's targeting.

**For future runtime-specific commands** (none in v1): the command source file moves out of
`src/HotRepl.UnityCommands/` and into one of the loader-specific subdirectories. The catalog static
class is the central place that knows which commands exist per runtime — and because the catalog
itself is in the shared folder, a `#if BEPINEX` or matching MSBuild DefineConstant on each csproj is
the canonical way to conditionally include a loader-specific command in the catalog list.

No runtime capability flag on the interface. The static-csproj-selection approach is simpler,
doesn't depend on C# 8 default interface methods (which pre-2021.2 Unity Mono doesn't support), and
removes a class of "command appears in tools/list but always errors out" agent confusions.

---

## 3. UnityCommands catalog (the 4 demo commands)

All four are deliberately small — each is one POCO arg type, one POCO result type, ~30 lines of
handler code. They demonstrate distinct architectural patterns; the next maintainer should be able
to copy any of them as a starting point for a new command.

### 3.1 `unity.app.info` — no-args sync read

```csharp
public sealed class UnityAppInfo {
    [Description("Application.productName as configured in the Unity project.")]
    public string ProductName { get; set; } = "";

    [Description("Unity engine version the game was built with.")]
    public string UnityVersion { get; set; } = "";

    [Description("RuntimePlatform enum value as string.")]
    public string Platform { get; set; } = "";

    [Description("True when running in the Unity editor (always false at runtime in a built game).")]
    public bool IsEditor { get; set; }
}

public sealed class UnityAppInfoCommand
    : IControlCommandHandler<EmptyArgs, UnityAppInfo>
{
    public string             Name         => "unity.app.info";
    public int                MajorVersion => 1;
    public ControlCommandKind Kind         => ControlCommandKind.Synchronous;
    public bool               MutatesState => false;

    public ValueTask<UnityAppInfo> ExecuteAsync(
        ControlCommandContext _, EmptyArgs __, CancellationToken ___)
        => new(new UnityAppInfo {
            ProductName  = UnityEngine.Application.productName,
            UnityVersion = UnityEngine.Application.unityVersion,
            Platform     = UnityEngine.Application.platform.ToString(),
            IsEditor     = UnityEngine.Application.isEditor,
        });
}
```

Demonstrates: simplest possible command. Copy when adding new no-arg read-only commands.

### 3.2 `unity.gameobject.find` — typed args, nullable result, IL2CPP divergence

```csharp
public sealed class UnityGameObjectFindArgs {
    [Required]
    [Description(
        "Hierarchy path. Two supported forms: " +
        "(1) plain name — uses GameObject.Find, may match any active root-or-tagged GO; " +
        "(2) slash-separated path starting with '/' — traverses from a scene root, " +
        "e.g. '/Player/Inventory/Slots'.")]
    public string Path { get; set; } = "";
}

public sealed class UnityGameObject {
    [Description("Name of the matched GameObject.")]
    public string Name { get; set; } = "";

    [Description("Active state in the hierarchy (activeInHierarchy).")]
    public bool ActiveInHierarchy { get; set; }

    [Description("Layer index.")]
    public int Layer { get; set; }

    [Description("Tag string.")]
    public string Tag { get; set; } = "";

    [Description("World-space position.")]
    public Vec3 Position { get; set; }

    [Description("Names of the components attached, in component-order.")]
    public string[] ComponentTypeNames { get; set; } = Array.Empty<string>();
}

public sealed class UnityGameObjectFindResult {
    [Description("Null when no GameObject matched the path.")]
    public UnityGameObject? GameObject { get; set; }
}

public sealed class UnityGameObjectFindCommand
    : IControlCommandHandler<UnityGameObjectFindArgs, UnityGameObjectFindResult>
{
    public string             Name         => "unity.gameobject.find";
    public int                MajorVersion => 1;
    public ControlCommandKind Kind         => ControlCommandKind.Synchronous;
    public bool               MutatesState => false;

    public ValueTask<UnityGameObjectFindResult> ExecuteAsync(
        ControlCommandContext _, UnityGameObjectFindArgs args, CancellationToken __)
    {
        var go = UnityEngine.GameObject.Find(args.Path);
        return new(new UnityGameObjectFindResult {
            GameObject = go is null ? null : ToDto(go),
        });
    }

    private static UnityGameObject ToDto(UnityEngine.GameObject go) {
        var components = go.GetComponents<UnityEngine.Component>();
        var names = new string[components.Length];
        for (int i = 0; i < components.Length; i++) {
            names[i] = components[i]?.GetType().FullName ?? "<null>";
        }
        return new UnityGameObject {
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

`Vec3` is a small POCO `{ float X, Y, Z }` defined in `HotRepl.UnityCommands.Core` to avoid the
schema generator hitting `HotRepl.UnityCommands.Core` to avoid the schema generator hitting
`UnityEngine.Vector3` (which has Unity-specific surface our generator would trip over).

**IL2CPP divergence:** the handler is shared in `Core`. The MelonLoader plugin's `csproj` references
the IL2CPP-unhollowed `UnityEngine.dll` so the `UnityEngine.GameObject` type resolves correctly at
compile time. The BepInEx plugin references the Mono `UnityEngine.dll`. The user-written code is
identical; the `csproj` references are what diverge. This is the case the spec exists to
demonstrate.

Demonstrates: typed args, nullable result, the loader-specific csproj split.

### 3.3 `unity.time.set_scale` — destructive-mutating sync

```csharp
public sealed class UnitySetTimeScaleArgs {
    [Required, Range(0f, 100f)]
    [Description(
        "New Time.timeScale value. 0 = paused, 1 = normal, 2 = double-speed, etc. " +
        "Values >1 may exceed safe physics-step bounds in some games.")]
    public float TimeScale { get; set; }
}

public sealed class UnitySetTimeScaleResult {
    [Description("Previous Time.timeScale value before this call.")]
    public float PreviousTimeScale { get; set; }

    [Description("Time.timeScale value after this call.")]
    public float NewTimeScale { get; set; }
}

public sealed class UnitySetTimeScaleCommand
    : IControlCommandHandler<UnitySetTimeScaleArgs, UnitySetTimeScaleResult>
{
    public string             Name         => "unity.time.set_scale";
    public int                MajorVersion => 1;
    public ControlCommandKind Kind         => ControlCommandKind.Synchronous;
    public bool               MutatesState => true;

    public ValueTask<UnitySetTimeScaleResult> ExecuteAsync(
        ControlCommandContext _, UnitySetTimeScaleArgs args, CancellationToken __)
    {
        float previous = UnityEngine.Time.timeScale;
        UnityEngine.Time.timeScale = args.TimeScale;
        return new(new UnitySetTimeScaleResult {
            PreviousTimeScale = previous,
            NewTimeScale      = UnityEngine.Time.timeScale,
        });
    }
}
```

Demonstrates: `MutatesState: true` propagating to the `destructiveHint` MCP annotation, `[Range]`
validation in the schema, returning the pre-mutation value for idempotent observability.

### 3.4 `unity.screenshot.capture` — artifact-producing sync

```csharp
public sealed class UnityScreenshotArgs {
    [Description("Optional super-size factor. Default 1.")]
    public int SuperSize { get; set; } = 1;
}

public sealed class UnityScreenshotResult {
    [Description("Width of the captured frame in pixels.")]
    public int Width { get; set; }

    [Description("Height of the captured frame in pixels.")]
    public int Height { get; set; }

    [Description("Reference to the PNG artifact. Read its bytes via the HotRepl artifact-read flow.")]
    public ArtifactRef Screenshot { get; set; } = new();
}

public sealed class UnityScreenshotCommand
    : IControlCommandHandler<UnityScreenshotArgs, UnityScreenshotResult>
{
    public string             Name         => "unity.screenshot.capture";
    public int                MajorVersion => 1;
    public ControlCommandKind Kind         => ControlCommandKind.Synchronous;
    public bool               MutatesState => false;

    public async ValueTask<UnityScreenshotResult> ExecuteAsync(
        ControlCommandContext context, UnityScreenshotArgs args, CancellationToken ct)
    {
        var superSize = Math.Max(1, args.SuperSize);

        // ScreenCapture.CaptureScreenshotAsTexture must run on the main thread —
        // our dispatcher already guarantees that for command_call.
        var tex = UnityEngine.ScreenCapture.CaptureScreenshotAsTexture(superSize);
        try {
            byte[] png = UnityEngine.ImageConversion.EncodeToPNG(tex);
            int w = tex.width, h = tex.height;

            var artifactRef = await context.Artifacts
                .WriteAsync("screenshot.png", png, contentType: "image/png", ct)
                .ConfigureAwait(false);

            return new UnityScreenshotResult {
                Width = w, Height = h, Screenshot = artifactRef,
            };
        } finally {
            UnityEngine.Object.Destroy(tex);
        }
    }
}
```

This is the only command that takes the `ControlCommandContext` parameter seriously — it uses
`context.Artifacts` to publish the binary payload to HotRepl's artifact store. Returning an
`ArtifactRef` instead of an inline byte array keeps the JSON-RPC envelope small and lets the agent
decide when to pay the cost of downloading the bytes.

If `context.Artifacts` doesn't already exist on `ControlCommandContext`, the spec proposes adding it
as part of this work — Ardenfall already produces artifacts (it writes JSON files referenced by
`ArtifactRef`s in its run results), so the substrate is there; we're just surfacing it as a typed
context API.

Demonstrates: artifact-producing handlers; the `context` parameter.

---

## 4. Mod template repo (`glockyco/hotrepl-mod-template`)

A fresh GitHub repository. Created via "New repository → repository template" so the "Use this
template" button is enabled.

### 4.1 Layout

```
hotrepl-mod-template/
├── .github/
│   ├── workflows/
│   │   └── ci.yml                — build BepInEx + MelonLoader projects
│   └── dependabot.yml             — keep NuGet refs current
├── .template.config/
│   └── template.json              — dotnet new manifest (§4.2)
├── src/
│   ├── MyMod.Core/                — netstandard2.1, the command handler(s)
│   │   ├── MyMod.Core.csproj
│   │   ├── Commands/
│   │   │   └── HelloWorldCommand.cs
│   │   └── README.md              — "your commands live here"
│   ├── MyMod.BepInEx/             — netstandard2.1, BepInEx plugin host
│   │   ├── MyMod.BepInEx.csproj
│   │   ├── Plugin.cs              — registration + config (§2.6 pattern)
│   │   └── PluginInfo.cs
│   └── MyMod.MelonLoader/         — net6.0, MelonLoader mod host
│       ├── MyMod.MelonLoader.csproj
│       └── Mod.cs
├── scripts/
│   ├── deploy-bepinex.sh          — copy DLL to game/BepInEx/plugins/
│   └── deploy-melonloader.sh      — copy DLL to game/Mods/
├── .editorconfig
├── .gitignore
├── Directory.Build.props          — shared TFM, treat warnings as errors, etc.
├── Local.props.example            — game path config (gitignored real copy)
├── LICENSE
├── MyMod.sln
└── README.md
```

`MyMod.*` is a placeholder. The `dotnet new` template substitutes it with the user's `-n` value. The
GitHub-template-button path doesn't rename automatically — the README's first step is "find/replace
MyMod with YourMod".

### 4.2 `dotnet new` manifest

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

Local install + first run:

```bash
dotnet new install ~/Projects/hotrepl-mod-template
dotnet new hotrepl-mod -n AwesomeMod -o ~/Projects/AwesomeMod \
  --PluginGuid awesomestudios.awesomemod --Author "AwesomeStudios"
```

After this, the user has `~/Projects/AwesomeMod/` with `AwesomeMod.sln`, all references hooked up,
one working demo command.

### 4.3 The demo command

```csharp
// src/MyMod.Core/Commands/HelloWorldCommand.cs
using HotRepl.Control;
using System.ComponentModel;
using System;
using System.ComponentModel.DataAnnotations;

namespace MyMod.Commands;

public sealed class HelloWorldArgs {
    [Description("Optional name. Defaults to 'world' if absent.")]
    public string? Name { get; set; }
}

public sealed class HelloWorldResult {
    [Description("The greeting that was produced.")]
    public string Greeting { get; set; } = "";

    [Description("Server-side timestamp when the greeting was generated, ISO 8601 UTC.")]
    public DateTimeOffset GeneratedAt { get; set; }
}

/// <summary>
/// Demo command — replace with your own.
///
/// This shows the canonical shape of a typed HotRepl command:
///   1. Define POCO args + result types. Decorate with [Description] / [Required] / [Range] etc.
///      to surface that metadata in the JSON schema HotRepl generates.
///   2. Implement IControlCommandHandler&lt;TArgs, TResult&gt;.
///   3. Register from the loader plugin (see MyMod.BepInEx/Plugin.cs or MyMod.MelonLoader/Mod.cs).
///
/// Invoke from a HotRepl client:
///   $ hotrepl run mymod.hello '{}'
///   $ hotrepl run mymod.hello '{"name":"Ardenfall"}'
/// </summary>
public sealed class HelloWorldCommand
    : IControlCommandHandler<HelloWorldArgs, HelloWorldResult>
{
    public string             Name         => "mymod.hello";
    public int                MajorVersion => 1;
    public ControlCommandKind Kind         => ControlCommandKind.Synchronous;
    public bool               MutatesState => false;

    public System.Threading.Tasks.ValueTask<HelloWorldResult> ExecuteAsync(
        ControlCommandContext _, HelloWorldArgs args, System.Threading.CancellationToken __)
    {
        var who = string.IsNullOrWhiteSpace(args.Name) ? "world" : args.Name;
        return new(new HelloWorldResult {
            Greeting    = $"Hello, {who}!",
            GeneratedAt = DateTimeOffset.UtcNow,
        });
    }
}
```

### 4.4 Loader plugins

`src/MyMod.BepInEx/Plugin.cs` — exact same `Config.Bind` + per-command opt-out pattern as
`HotRepl.UnityCommands` (§2.6). New plugin authors learn the idiom from a working production-grade
reference, not "here's the minimum."

`src/MyMod.MelonLoader/Mod.cs` — same shape with `MelonPreferences`.

### 4.5 Build / deploy scripts

`scripts/deploy-bepinex.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail
# Load Local.props to find the game install path
GAME_PATH=$(grep -oE 'GamePath>[^<]+' Local.props | cut -d'>' -f2)
dotnet build src/MyMod.BepInEx -c Debug --nologo -v q
cp -f src/MyMod.BepInEx/bin/Debug/netstandard2.1/MyMod.*.dll \
      "$GAME_PATH/BepInEx/plugins/MyMod/"
echo "Deployed."
```

(MelonLoader sibling is structurally identical with `Mods/` instead of `BepInEx/plugins/MyMod/`.)

Deliberately bash, not a `.csproj` `<Target>`: scripts are easier to read, debug, and adapt for the
consumer's specific OS / game-install layout.

### 4.6 README

The template's README is the **single most important file in this work** for new-developer
experience. Outline:

1. **"What you're getting"** — three sentences. A working BepInEx+MelonLoader mod that talks to a
   Unity game via HotRepl.
2. **Prerequisites** — .NET SDK, BepInEx or MelonLoader installed in your game.
3. **Quickstart (3 commands)** — clone or `dotnet new`, edit `Local.props`, run
   `scripts/deploy-bepinex.sh`.
4. **Project layout** — table mapping each folder to its purpose.
5. **Adding a new command** — link to `HelloWorldCommand.cs`, explain that "copy this file, change
   the names" is the supported workflow.
6. **Schema authoring** — what `[Description]` / `[Required]` / `[Range]` do.
7. **Mono vs IL2CPP** — when you need separate code paths (you usually don't); link to
   `HotRepl.UnityCommands.MelonLoader/Plugin.cs` as the reference for "what diverges."
8. **Versioning your mod** — bump `Directory.Build.props`'s `<Version>`.

---

## 5. Ardenfall migration

### 5.1 Scope

In `~/Projects/ardenfall-compendium`:

- Migrate all 10 handlers in `mod/src/Control/Handlers/` to
  `IControlCommandHandler<TArgs, TResult>`.
- Define POCO arg/result types in `mod/src/Control/Models/` (new folder).
- Delete `mod/src/Control/CompendiumCommandSchemas.cs` entirely (no more hand-written `JObject`
  schema literals).
- Update `mod/src/Control/CompendiumCommandRegistry.cs` registration calls to use the typed
  `Register<TArgs, TResult>` extension.
- The mod's setup scripts (`mod/scripts/copy-libs.sh`) require no new copied libraries — the
  hand-rolled schema generator in §2.3 means HotRepl.Core ships with zero new third-party deps. The
  mod itself just gets a richer set of attributes available on its POCOs.

### 5.2 Per-handler outline

The 10 handlers, current → new:

| Current handler              | New typed shape                                    | Notes                                                                              |
| ---------------------------- | -------------------------------------------------- | ---------------------------------------------------------------------------------- |
| `CompendiumInfoCommand`      | `<EmptyArgs, CompendiumInfo>`                      | Trivial.                                                                           |
| `CompendiumPreflightCommand` | `<EmptyArgs, PreflightReport>`                     | `PreflightReport` POCO already exists in `mod/src/Dtos/PreflightReport.cs`; reuse. |
| `ContinueFromMenuCommand`    | `<EmptyArgs, ContinueFromMenuResult>`              | Job pattern.                                                                       |
| `RunBeginCommand`            | `<RunBeginArgs, RunBegun>`                         | Job. `RunBeginArgs` has a single `string Slug` parameter.                          |
| `RunStatusCommand`           | `<RunStatusArgs, RunStatus>`                       | Sync read-only.                                                                    |
| `EntityPlanCommand`          | `<EntityPlanArgs, EntityPlan>`                     | Sync read-only.                                                                    |
| `EntityExportBatchCommand`   | `<EntityExportBatchArgs, EntityExportBatchResult>` | Job.                                                                               |
| `RunFinalizeCommand`         | `<RunFinalizeArgs, RunFinalizeResult>`             | Job.                                                                               |
| `RunDiscardCommand`          | `<RunDiscardArgs, RunDiscardResult>`               | Sync, mutating.                                                                    |
| `GameQuitCommand`            | `<EmptyArgs, GameQuitResult>`                      | Sync, mutating.                                                                    |

Each handler's body is unchanged (the implementation logic is fine). The diff per handler is
roughly:

- Remove the `Descriptor` property's hand-written `JObject` schema args.
- Add `string Name`, `int MajorVersion`, `ControlCommandKind Kind`, `bool MutatesState` simple
  properties.
- Change the signature to typed `ValueTask<TResult> ExecuteAsync(…,
  TArgs args, …)`.
- Replace the `JObject args` parsing with direct use of the typed args parameter.
- Replace the `new ControlCommandResult(jObjectOutput, …)` with `return new TResult { … }`.

The wire protocol is unchanged — the adapter takes care of serialization in both directions.
Existing CLI/MCP/agent clients continue to work without any change.

### 5.3 Things the migration also enables (free wins)

1. **Schema descriptions surface to MCP.** Today, agents see
   `"argsSchema":
   { "type": "object" }`. After migration they see real property descriptions on
   every argument. `tools/list` output becomes self-documenting.
2. **Inline-validatable args at the agent boundary.** Today, an agent sending `{"slug": 42}` to
   `RunBeginCommand` gets a "command failed: serialization error" deep in the handler. After
   migration the adapter rejects it at the boundary with a precise schema-failure error.
3. **Schema drift is impossible going forward.** The schema IS the type.

---

## 6. Ancient Kingdoms migration

### 6.1 Scope: deletions

In `~/Projects/ancient-kingdoms-mods`:

- Delete `mods/AutoExporter/` (whole project).
- Delete `mods/DataExporter/ExportResultFile.cs`.
- Delete `build-tool/Game/ExportResultReader.cs`.
- Delete `tests/DataExporter.Tests/ExportResultFileTests.cs`.
- Delete `exported-data/.exporter-result.json` (gitignored artifact).
- Delete the unfinished plan at `docs/superpowers/plans/2026-05-22-ak-hotrepl-v2-migration.md`
  (superseded by this spec + its plan).
- Remove `--export-data` and `--export-screenshots` CLI flag handling from whatever launches the
  game (likely the existing `build-tool launch`).

### 6.2 Scope: additions

A new MelonLoader mod project at `mods/HotReplCommands/`:

```
mods/HotReplCommands/
├── HotReplCommands.csproj
├── Mod.cs                          — MelonMod that registers commands on init
├── Commands/
│   ├── GameInfoCommand.cs          — ak.game.info (sync)
│   ├── PlayerPositionCommand.cs    — ak.player.position (sync)
│   ├── WorldEnterCommand.cs        — ak.world.enter (job)
│   ├── ExportRunCommand.cs         — ak.export.run (job)
│   └── ExportScreenshotsCommand.cs — ak.export.screenshots (job)
└── Models/
    ├── GameInfo.cs
    ├── PlayerPosition.cs
    ├── WorldEnterResult.cs
    ├── ExportRunResult.cs           — same shape as today's ExportRunResult, just typed
    └── ExportScreenshotsResult.cs
```

### 6.3 The 5 commands

| Command                 | Pattern        | What it does                                                                                                                                                                                                                                                                             |
| ----------------------- | -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ak.game.info`          | sync read-only | Returns game version, current zone name, network state.                                                                                                                                                                                                                                  |
| `ak.player.position`    | sync read-only | Returns the local player's `(x, y, z)` world position. Demonstrates IL2CPP unhollowed game-type lookup (`Il2CppMirror.NetworkClient.localPlayer` is a game-defined unhollowed type).                                                                                                     |
| `ak.world.enter`        | **job**        | Owns the scene-state automation that `AutoExporter` used to do: click singleplayer → first-character select → wait for `NetworkClient.localPlayer != null` → 3 second settle. Returns terminal status when the world is "ready for inspection."                                          |
| `ak.export.run`         | **job**        | The full DataExporter pipeline. Internally calls the existing `DataExporter.ExportAllData()`. Returns the same `ExportRunResult` shape that today gets written to `.exporter-result.json`, but as the command's `output`. Per-exporter progress reported via the job's `progress` field. |
| `ak.export.screenshots` | **job**        | Wraps `MapScreenshotter.StartScreenshotCapture()` + waits for `IsCapturing == false`. Returns counts + duration.                                                                                                                                                                         |

The Shift+F9 keybind in `DataExporter` stays as a convenience for manual runs. It calls
`ExportAllData()` directly without going through HotRepl — keeps the manual workflow working for
anyone offline-testing.

### 6.4 `build-tool export` rewrite

Today's flow:

1. `build-tool export` launches the game with `--export-data` (and optionally
   `--export-screenshots`).
2. `AutoExporter` reaches the World scene and calls `DataExporter.ExportAllData()`.
3. `DataExporter` writes `.exporter-result.json`.
4. `AutoExporter` quits the game.
5. `build-tool` watches `.exporter-result.json`, reads it, returns.

New flow:

1. `build-tool export` launches the game (no special flags).
2. `build-tool export` connects to `ws://127.0.0.1:18590`.
3. Runs `ak.world.enter` — polls `job_status` until terminal.
4. Runs `ak.export.run` — polls `job_status` until terminal.
5. Optionally runs `ak.export.screenshots` — polls until terminal.
6. Reads the structured `output` from each job's terminal result.
7. Quits the game (either via a new `ak.game.quit` typed command, or by killing the process — TBD in
   the plan).

`build-tool` uses the existing `HotRepl` namespace as the directory for HotRepl-specific C# code;
gets a new `HotReplClient` (≤200 LOC over Fleck or ClientWebSocket — we don't pull in
`@hotrepl/sdk`, this is C#-only). The client implements only the message types AK uses: `handshake`,
`command_call`, `job_status`, `job_status_result`, `job_result`, `session_evicted`, `error`.

Naming the new typed-command mod: `HotReplCommands` is intentionally generic. If the AK project ever
wants to add commands beyond the export flow, they go in the same mod.

### 6.5 What stays

- `mods/DataExporter/` — keeps its `ExportAllData()` entry point and the Shift+F9 keybind. Only the
  result-file writing is removed; the in-memory `ExportRunResult` it produces becomes the
  `ak.export.run` command's output.
- `mods/MapScreenshotter/` — completely unchanged. `ExportScreenshotsCommand` just calls into it.
- All 9 other AK mods — out of scope.

---

## 7. Erenshor migration

### 7.1 Scope

In `~/Projects/Erenshor`, only `src/mods/MapTileCapture/` is touched. The custom Fleck WebSocket
server and bespoke message protocol are deleted; the mod becomes a thin HotRepl command host.

**Deletions:**

- `src/mods/MapTileCapture/src/Server/CaptureWebSocketServer.cs`
- `src/mods/MapTileCapture/src/Protocol/Messages.cs` — converted to POCO command arg/result types in
  `src/mods/MapTileCapture/src/Models/`.
- `src/mods/MapTileCapture/src/Plugin.cs` — rewritten to register commands with HotRepl instead of
  standing up its own server.

**Out of scope (per maintainer decision):**

- `src/mods/InteractiveMapCompanion/` — keeps its own WebSocket server.
- `src/Assets/Editor/` — Unity-Editor-driven static data extraction; runs outside the game.
- The 4 other Erenshor mods.

### 7.2 New typed commands

| Command                          | Pattern        | Wraps                                                                                                                                                               |
| -------------------------------- | -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `erenshor.map.probe_zone_bounds` | sync read-only | `ZoneBoundsProbe` — returns the bounding rectangle of the current zone.                                                                                             |
| `erenshor.map.capture_chunks`    | **job**        | `ChunkRenderer` — captures one or more chunks specified by the `ChunkSpec[]` arg array. Each chunk's PNG returned as a named artifact in the job's terminal result. |

The existing `ChunkSpec` and `ExclusionRule` types in `Protocol/Messages.cs` become POCO arg-type
fields with `[Required]` / `[Description]` annotations.

### 7.3 Python pipeline integration

Today: Python (somewhere in `~/Projects/Erenshor/scripts/` or similar) opens its own WebSocket to
MapTileCapture's custom server and exchanges custom messages.

New: Python `subprocess`-shells out to `bunx @hotrepl/cli`:

```python
import subprocess, json

# Probe zone bounds (sync command).
result = subprocess.run(
    ["bunx", "@hotrepl/cli", "run", "erenshor.map.probe_zone_bounds", "{}", "--format", "json"],
    capture_output=True, text=True, check=True,
)
zone_bounds = json.loads(result.stdout)

# Capture chunks (job command).
chunks_json = json.dumps({"chunks": [{"index": 0, "centerX": 0.0, ...}, ...]})
result = subprocess.run(
    ["bunx", "@hotrepl/cli", "run", "erenshor.map.capture_chunks", chunks_json, "--format", "json"],
    capture_output=True, text=True, check=True,
)
job_output = json.loads(result.stdout)

# Read PNG artifacts referenced from job_output.
for chunk in job_output["chunks"]:
    artifact_ref_json = json.dumps(chunk["png"])
    png_bytes_b64 = subprocess.check_output(
        ["bunx", "@hotrepl/cli", "artifacts", "read", artifact_ref_json, "--format", "json"],
    )
    # ...write PNG to disk...
```

Tradeoffs:

- Pro: zero new Python code to maintain. The CLI is the surface. If the protocol changes, the Python
  code keeps working as long as the command args/result shapes are stable.
- Pro: matches HotRepl v2's stated policy ("no Python SDK"). Erenshor was the implicit holdout that
  motivated keeping the custom protocol; that pressure goes away here.
- Pro: subprocess + JSON parsing is universally understood. New Python contributors to Erenshor
  don't need to learn anything HotRepl-specific.
- Con: process-spawn overhead per command. For map capture (which already takes seconds per chunk),
  this is rounding error. If a hot loop ever needs sub-millisecond overhead, we revisit.
- Con: `bunx` requires Bun on PATH. Document this as a prerequisite in Erenshor's README.

---

## 8. Operational concerns

### 8.1 Versioning

UnityCommands ships **in lockstep with HotRepl.Core**. One repo-wide tag (e.g., `v2.1.0`) drives
every artifact in the HotRepl repo — the npm packages, the C# DLLs, and UnityCommands.

The reasoning: UnityCommands depends on `HotRepl.Core` internals (the new generic interface, schema
cache, `ControlCommandContext.Artifacts`). No practical scenario where a consumer wants
"UnityCommands 2.5 against HotRepl.Core 2.3." Independent versioning is the right answer for
**external** plugin ecosystems with third-party authors; UnityCommands is a first-party companion
and is correctly modeled as part of HotRepl itself.

The template repo lives outside the HotRepl repo and versions on its own clock. Its only HotRepl
coupling is at the API surface (the `IControlCommandHandler<,>` interface), which is stable across
HotRepl patch + minor releases.

### 8.2 Distribution

Recap of §2.7: UnityCommands DLLs land in the host's build output via MSBuild project reference. No
code changes to consumer deploy scripts; they all already pick up "everything in `bin/`." The AK
migration in §6 updates its explicit allowlist in `HotReplDeployer.cs` to include
`HotRepl.UnityCommands.*.{dll,pdb}`. No new third-party dependencies need allowlisting; the
hand-rolled schema generator means HotRepl.Core ships clean.

### 8.3 Configuration

Recap of §2.6:

- BepInEx: `Config.Bind` on `[General] Enabled` + `[Commands] Disabled` in
  `BepInEx/config/hotrepl.unitycommands.bepinex.cfg`.
- MelonLoader: `MelonPreferences.CreateCategory("HotRepl.UnityCommands")` with same two entries.
- Read-once at startup; restart-required for changes; document in config description text.

The template repo demonstrates the same pattern for its own demo command's config category. That's a
meaningful piece of the template's value.

### 8.4 BepInEx & MelonLoader plugin identity & ordering

|                  | BepInEx                                                          | MelonLoader                                                                                                                                                                                 |
| ---------------- | ---------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Plugin GUID      | `hotrepl.unitycommands.bepinex`                                  | (informational only — `MelonInfo` has no GUID field; the assembly name is the identity)                                                                                                     |
| Friendly name    | `HotRepl Unity Commands`                                         | `HotRepl Unity Commands`                                                                                                                                                                    |
| Ordering         | `[BepInDependency("hotrepl.bepinex", HardDependency)]`           | Register from `OnLateInitializeMelon()`, which fires after every `OnInitializeMelon` across all mods. Defensive null-check on `GlobalControlCommandRegistry.Instance` for misordered loads. |
| Config namespace | `[General]` and `[Commands]` sections in a dedicated `.cfg` file | `HotRepl.UnityCommands` category in `MelonPreferences.cfg`                                                                                                                                  |

---

## 9. Out of scope (explicit)

(Restated from §1.2, for reviewers who jumped here.)

- Hot config reload at runtime.
- Tier 2/3 commands in UnityCommands (anything beyond the 4 demo commands). Including all the things
  UnityExplorer/RuntimeUnityEditor expose: scene-tree dump, component dump, component-property
  write, asset find, input simulation, recording, camera control, log tail.
- Publishing the template repo to NuGet.
- Publishing UnityCommands to Thunderstore.
- Migrating Erenshor's `InteractiveMapCompanion`, Unity Editor extraction, or any other Erenshor
  mod.
- Migrating Ardenfall's non-typed-command surface (none exists).
- Migrating AK's other 11 mods.
- A second non-Unity demo plugin.
- Per-command rate limiting / permission gates.
- Generic `IControlCommandHandler<TArgs, TResult, TArtifacts>` for typed artifact maps.
- A runtime `SupportedRuntime` capability flag on the typed interface. Static csproj selection
  (§2.8) handles per-runtime filtering at compile time.

---

## 10. Verification plan per repo

A high-level pass; the full plan file will spell out exact commands and test names.

### 10.1 HotRepl

- New xUnit tests in `tests/HotRepl.Tests/Unit/`:
  - `TypedControlCommandAdapterTests` — round-trip with `EmptyArgs`, nullable result, `[Required]`
    violation, `Range` violation, attribute-driven schema generation, `[JsonProperty(Name)]`
    honoring.
  - `SchemaBuilderTests` — primitives, nullables, arrays, dictionaries, enums, nested POCOs,
    fallback path for unsupported types.
  - `SchemaCacheTests` — single-generation per type, EmptyArgs special case, AnyObject sentinel.
  - `UnityCommandsCatalogTests` — exactly 4 handlers, names match spec.
- Updated package tests in `packages/conformance/test/` — confirm the protocol-level descriptor JSON
  is unchanged for migrated commands (the adapter must produce wire-identical output to a
  hand-written descriptor).
- `lefthook run pre-push --force` clean.
- Manual: build HotRepl host, deploy to Ardenfall, run live verification harness — confirm the 4
  UnityCommands tools show up in `commands_list`.

### 10.2 Mod template repo

- GitHub Actions CI builds the unmodified template against the current HotRepl release.
- Manual:
  `dotnet new install <path> && dotnet new hotrepl-mod -n
  TestMod -o /tmp/TestMod && cd /tmp/TestMod && dotnet build`
  — clean build.
- Manual: deploy TestMod to a game install via its `scripts/deploy-*.sh`, launch the game, confirm
  `mymod.hello` shows up in `hotrepl list-commands`.

### 10.3 Ardenfall

- Existing C# unit tests in `mod-tests/` all pass.
- Live verification harness in HotRepl (`scripts/verify-live-ardenfall.ts`) passes 9/9 against
  migrated Ardenfall mod.
- `bun run hotrepl:setup` succeeds.

### 10.4 AK

- New tests in `tests/HotReplCommands.Tests/` — schema round-trips for each command's POCOs.
- `build-tool` tests in `tests/BuildTool.Tests/` updated to test the new HotRepl-driven export path
  (mocking the HotRepl client). The `.exporter-result.json` test files are deleted.
- Manual: `dotnet run --project build-tool export` end-to-end against the game.

### 10.5 Erenshor

- C# build of `src/mods/MapTileCapture/` clean.
- Python pipeline driving `bunx @hotrepl/cli` end-to-end against the game produces tiles
  bitwise-identical to today's tiles (regression guard).

---

## 11. Open questions

Things I expect the user to push back on or refine before the plan-writing phase starts:

1. **`ControlCommandContext.Artifacts` API surface.** §3.4's screenshot command assumes the
   dispatcher exposes an `Artifacts.WriteAsync(name,
   bytes, contentType, ct)` method. Ardenfall
   today produces artifacts via a different path. The exact API needs to be designed in the plan;
   I'm provisionally proposing the simplest async-byte-array form, but a stream-based variant is
   also reasonable.

2. **MelonLoader hook for deferred init.** §2.5 commits to `OnLateInitializeMelon()` as the
   registration hook. Plan should verify the exact MelonLoader version HotRepl currently targets
   supports this hook (introduced in 0.6; the `OnApplicationStart` it replaced is gone in 0.7.0). If
   HotRepl is somehow pinned to pre-0.6, we have a real problem; otherwise we're fine.

3. **Should the BepInEx UnityCommands plugin ship a no-op stub on MelonLoader-only games (and vice
   versa)?** Today BepInEx and MelonLoader are mutually exclusive — a game uses one or the other. No
   stub needed; confirming.

4. **Resolved in §12 self-review.** Original concern about `SupportedRuntime`
   default-interface-method — dropped entirely in favor of static csproj selection (§2.8).

5. **Erenshor's "kill the game" pattern.** §6.4 mentions "either a new `ak.game.quit` typed command,
   or by killing the process." Both work; I'd default to a typed `ak.game.quit` because it's
   cleaner. Same question for Erenshor's existing pipeline — does it need to quit the game from the
   Python driver? Plan will resolve.

6. **Where do POCO arg/result types live in `HotRepl.UnityCommands.Core`?**
   `Commands/UnityAppInfo.cs` (one file per command) or `Models/UnityAppInfo.cs` (separate types
   folder)? Style preference; I'd go with the latter for parity with Ardenfall's existing
   `mod/src/Dtos/`.

7. **Vec3 / Vector3 boundary.** §3.2 proposes a POCO `Vec3` to avoid the schema generator hitting
   `UnityEngine.Vector3` (which has Unity-specific surface our hand-rolled generator falls back on).
   Plan will verify whether a `JsonConverter`-based path lets us serialize `Vector3` directly
   without leaking the engine surface into the schema; otherwise `Vec3` is the right boundary.

---

## 12. Self-review log (v0.1 → v0.2 corrections)

After the v0.1 draft was written, a research-driven self-review pass found three real architectural
risks. All are patched in the spec above; this section records what changed and why for future
readers.

### 12.1 Schema generation: NJsonSchema → hand-rolled (§2.3, §2.4)

**v0.1** proposed NJsonSchema as the schema-from-POCO library. Self-review discovered NJsonSchema
11.x declares a transitive `System.Text.Json (>= 9.0.9)` dependency (even the
`NJsonSchema.NewtonsoftJson` sub-package pulls it). Mono BepInEx games have documented
`System.Text.Json` assembly-loading issues; shipping STJ + its dependency chain into every
consumer's plugin folder is real, avoidable deployment risk.

Considered alternatives:

- **NJsonSchema 10.x** — pre-STJ-dep but unmaintained. Brittle long-term.
- **Newtonsoft.Json.Schema (Newton King)** — dual-licensed commercial + AGPL. AGPL incompatible with
  HotRepl's MIT license. Hard block.
- **Hand-rolled minimal generator** — ~200 LOC of reflection over POCO public properties, supporting
  only the attributes we actually need (`[Required]`, `[Range]`, `[Description]`, `[JsonProperty]`).
  Zero third-party blast radius, immune to upstream library churn, exact control over schema output.
  Chosen.

### 12.2 SupportedRuntime as a default interface member: dropped (§2.1, §2.8)

**v0.1** proposed a `ControlCommandRuntime SupportedRuntime` default interface member for filtering
commands by runtime at registration. Two problems found in self-review:

1. Unity Mono before 2021.2 doesn't support C# 8 default interface members. HotRepl targets
   `netstandard2.1` but the runtime executing the compiled IL is the GAME's Mono — and we don't know
   which Unity version every downstream consumer uses.
2. With shared-source compilation (§12.3 below), a command that can only work on one runtime can
   simply be excluded from the other csproj's compile set. The runtime filter never fires.

Resolution: drop the property entirely. Static csproj selection is the canonical mechanism for "this
command only works on runtime X."

### 12.3 Plugin layout: shared-source, not Core+Loader csprojs (§2.5)

**v0.1** proposed three csprojs: `HotRepl.UnityCommands.Core` (cross-runtime shared), plus
loader-specific `BepInEx` and `MelonLoader` csprojs. Self-review realized the `Core` csproj would
have to reference `UnityEngine.dll` — which forces it to pick Mono-flavored or
IL2CPP-unhollowed-flavored, and those are different assemblies. Both loader csprojs can't both
reference the same Core csproj if the Core references the wrong UnityEngine.

Resolution: replace the three-csproj layout with **shared-source compilation** — a
`src/HotRepl.UnityCommands/` source folder with no .csproj of its own, plus two loader-specific
csprojs that `<Compile Include="..\HotRepl.UnityCommands\**\*.cs" />` the same source files against
their respective UnityEngine references. This is the established pattern for dual-runtime Unity mods
(UnityExplorer, RuntimeUnityEditor, BepInEx's own bridge plugins all use it).

### 12.4 MelonLoader lifecycle: OnLateInitializeMelon, not OnApplicationStart (§2.5)

**v0.1** proposed using `OnApplicationStart` as the deferral point if
`GlobalControlCommandRegistry.Instance` is null at `OnInitializeMelon`. Self-review confirmed
MelonLoader v0.7.0 made obsolete members into compile errors and `OnApplicationStart` is one of the
removed members (it was deprecated in 0.6 in favor of `OnLateInitializeMelon`).

Resolution: use `OnLateInitializeMelon()` (introduced 0.6, called after all `OnInitializeMelon`
calls across all mods). Defensive null-check on the registry remains for catastrophic loader
misordering.

### 12.5 What didn't change

- Lockstep versioning with `HotRepl.Core` (§8.1).
- Bundled distribution via host-project reference (§8.2).
- Per-loader native config (BepInEx `Config.Bind`, MelonLoader `MelonPreferences`) with `Enabled`
  master switch + `Disabled` CSV opt-out (§2.6).
- Read-once-at-startup config policy; restart-required for changes.
- The 4-command catalog and the architectural pattern each demonstrates (§3).
- Migration scopes for Ardenfall (1:1), AK (full replacement of AutoExporter +
  `.exporter-result.json` round-trip), Erenshor (MapTileCapture only, Python shells out to
  `bunx @hotrepl/cli`) (§5-§7).
- Template repo layout: GitHub template + embedded `dotnet new` manifest, not NuGet-published on day
  1 (§4).

### 12.6 Schema generator: hand-rolled → NJsonSchema 10.9.0 + ILRepack (v0.2 → v0.3)

**v0.2** committed to a hand-rolled ~200-LOC schema generator in `HotRepl.Core` to avoid NJsonSchema
11.x's transitive `System.Text.Json` dependency. External annotation review pushed back that this is
scope creep: HotRepl is a runtime REPL, not a schema-library author. JSON Schema has rich edge cases
("I only need a subset" predictably grows over time) and rolling our own opens a maintenance front
we shouldn't take on.

Subsequent maintainer note: `System.Text.Json` doesn't work cleanly on Unity Mono — assembly load +
JIT issues across plugins. This rules out the v0.1 ILRepack-internalize-NJsonSchema-11.x escape
hatch too, because internalization hides the assembly-resolution problem but cannot fix STJ's
runtime-compatibility problem.

The robust answer is **NJsonSchema pinned to `10.9.0`** — the last release before STJ was added as a
transitive dependency in 11.0. 10.x uses only `Newtonsoft.Json` and `Namotion.Reflection`, both pure
.NET and known-good on Unity Mono. The 10.x API surface for POCO → schema is feature-complete and
stable; the move to 11.x was driven by STJ-attribute support, which we don't need.

To keep the consumer-facing dependency picture identical to today, NJsonSchema 10.9.0 +
Namotion.Reflection are **ILRepack-internalized into `HotRepl.Core.dll`** at build time. Consumers
continue to see exactly one HotRepl.Core DLL plus the Newtonsoft.Json they already shipped with.

The HotRepl `AGENTS.md` rule against ILRepack is preserved in spirit: it forbids internalizing
**consumer-facing** assemblies (Core itself, Newtonsoft, Fleck) where type-identity collapse would
break what consumers compile against. NJsonSchema is purely internal — no consumer ever writes
`using NJsonSchema;` — so internalizing it is exactly what the tool was designed for.

Bounded long-term risk: NJsonSchema 10.x is feature-frozen ("legacy" only in that the author moved
to 11.x for STJ users). If a critical bug ever needs fixing, escape paths are (a) upstream PR (the
author is responsive), (b) fork the bits we need (it's MIT-licensed), or (c) migrate to a build-time
source generator that runs NJsonSchema 11.x on CoreCLR at compile time and emits precomputed schema
strings into the build output (no runtime third-party at all). None of these is urgent for v1.
