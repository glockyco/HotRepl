using System;
using System.Collections.Generic;
using System.Reflection;
using HotRepl.Control;
using HotRepl.Evaluator;
using HotRepl.Evaluator.Roslyn;

namespace HotRepl.Tests.Integration;

/// <summary>
/// A host whose engine evaluates code on the test runtime. Mono.CSharp cannot emit assemblies
/// on .NET 10, so engine tests that need eval results use the Roslyn script evaluator.
/// </summary>
internal sealed class RoslynEngineHost : IReplHost
{
    public RoslynEngineHost(int port) => Config.Port = port;

    public ReplConfig Config { get; } = new();
    public HostInfo HostInfo { get; } = new() { Name = "Integration test" };
    public IControlCommandRegistry ControlCommands => EmptyControlCommandRegistry.Instance;
    public IReadOnlyList<EvaluatorCapabilities> AvailableEvaluators =>
        RoslynEvaluatorFactory.Capabilities;
    public string DefaultEvaluatorName => RoslynScriptEvaluator.ScriptCapabilities.Name;
    public IReadOnlyList<Assembly> AdditionalAssemblies => Array.Empty<Assembly>();
    public IReadOnlyList<string> AdditionalUsings => Array.Empty<string>();
    public string[] AdditionalHelperSignatures => Array.Empty<string>();

    public ICodeEvaluator CreateEvaluator(string evaluatorName) =>
        RoslynEvaluatorFactory.Create(evaluatorName, this);

    public ConsoleCapture CreateConsoleCapture() => new ConsoleRedirectCapture();

    public void LogInfo(string message) { }

    public void LogDebug(string message) { }

    public void LogWarning(string message) { }

    public void LogError(string message, Exception? ex = null) { }
}
