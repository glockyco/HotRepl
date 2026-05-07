#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HotRepl.Control;
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
        public IControlCommandRegistry ControlCommands => EmptyControlCommandRegistry.Instance;
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
