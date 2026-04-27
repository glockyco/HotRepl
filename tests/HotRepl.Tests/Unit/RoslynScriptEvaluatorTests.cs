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
