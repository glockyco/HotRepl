using System;
using System.Collections.Generic;
using System.Reflection;
using HotRepl.Control;
using HotRepl.Control.Internal;
using HotRepl.Evaluator;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ControlCommandRegistryTests
{
    [Fact]
    public void EmptyRegistry_DescribesNoCommands()
    {
        Assert.Empty(EmptyControlCommandRegistry.Instance.Describe());
        Assert.False(
            ((ICompiledRegistry)EmptyControlCommandRegistry.Instance).TryGet(
                "archive.preflight",
                out _
            )
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Descriptor_RequiresNonEmptyName(string name)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new ControlCommandDescriptor(
                name,
                version: 1,
                kind: ControlCommandKind.Synchronous,
                mutatesState: false,
                argsSchema: JObject.Parse("{\"type\":\"object\"}"),
                resultSchema: JObject.Parse("{\"type\":\"object\"}")
            )
        );

        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public void Descriptor_RequiresPositiveVersion()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ControlCommandDescriptor(
                "archive.preflight",
                version: 0,
                kind: ControlCommandKind.Synchronous,
                mutatesState: false,
                argsSchema: JObject.Parse("{\"type\":\"object\"}"),
                resultSchema: JObject.Parse("{\"type\":\"object\"}")
            )
        );

        Assert.Equal("version", ex.ParamName);
    }

    [Fact]
    public void Host_CanExposeEmptyRegistry()
    {
        var host = new TestHost();

        Assert.Same(EmptyControlCommandRegistry.Instance, host.ControlCommands);
    }

    private sealed class TestHost : IReplHost
    {
        public ReplConfig Config { get; } = new();
        public HostInfo HostInfo { get; } =
            new()
            {
                Name = "Tests",
                Version = "1",
                Runtime = ".NET",
                Platform = "Unit",
            };
        public IReadOnlyList<EvaluatorCapabilities> AvailableEvaluators { get; } =
            Array.Empty<EvaluatorCapabilities>();
        public string DefaultEvaluatorName => "none";
        public IControlCommandRegistry ControlCommands => EmptyControlCommandRegistry.Instance;
        public IReadOnlyList<Assembly> AdditionalAssemblies { get; } = Array.Empty<Assembly>();
        public IReadOnlyList<string> AdditionalUsings { get; } = Array.Empty<string>();
        public string[] AdditionalHelperSignatures { get; } = Array.Empty<string>();

        public ICodeEvaluator CreateEvaluator(string evaluatorName) =>
            throw new NotSupportedException();

        public void LogInfo(string message) { }

        public void LogDebug(string message) { }

        public void LogWarning(string message) { }

        public void LogError(string message, Exception? ex = null) { }
    }
}
