using HotRepl;
using HotRepl.Evaluator;
using HotRepl.Evaluator.MonoCSharp;
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
        var capabilities = MonoCSharpEvaluator.MonoCapabilities;

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
