using System.Linq;
using HotRepl.Control;
using HotRepl.Control.Artifacts;
using Xunit;

namespace HotRepl.Tests.Unit;

/// <summary>
/// Packaging guards for the merged <c>HotRepl.Core.dll</c>. NJsonSchema and its
/// <c>Namotion.Reflection</c> dependency are internalized into Core at build time
/// via ILRepack so downstream consumers (BepInEx, MelonLoader, mod template) see
/// one Core DLL plus Newtonsoft / Fleck side-by-side, with no Namotion sidecar.
/// </summary>
public class CoreAssemblyPackagingTests
{
    [Fact]
    public void HotReplCoreAssembly_DoesNotReferenceNJsonSchema()
    {
        var core = typeof(ControlCommandResult).Assembly;
        var referenced = core.GetReferencedAssemblies().Select(name => name.Name).ToArray();
        Assert.DoesNotContain("NJsonSchema", referenced, System.StringComparer.Ordinal);
    }

    [Fact]
    public void HotReplCoreAssembly_DoesNotReferenceNamotionReflection()
    {
        var core = typeof(ControlCommandResult).Assembly;
        var referenced = core.GetReferencedAssemblies().Select(name => name.Name).ToArray();
        Assert.DoesNotContain("Namotion.Reflection", referenced, System.StringComparer.Ordinal);
    }

    [Fact]
    public void ArtifactRefLogicalNameSetter_PreservesCanonicalIsExternalInitModreq()
    {
        // ILRepack with duplicate-marker handling must not rename Core's own
        // IsExternalInit polyfill when merging Namotion's copy. A GUID-suffixed
        // type name in this modreq means the merged Core's public init-setter
        // metadata no longer matches the canonical compiler contract.
        var property = typeof(ArtifactRef).GetProperty(nameof(ArtifactRef.LogicalName))!;
        var setter = property.GetSetMethod(nonPublic: true)!;
        var modreqs = setter.ReturnParameter.GetRequiredCustomModifiers();

        Assert.Single(modreqs);
        Assert.Equal("System.Runtime.CompilerServices.IsExternalInit", modreqs[0].FullName);
    }
}
