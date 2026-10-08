using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HotRepl.Control;
using HotRepl.Evaluator;
using HotRepl.Evaluator.MonoCSharp;
using HotRepl.Helpers;
using HotRepl.Helpers.Unity;
using Lunaris;

namespace HotRepl.Host.Lunaris;

/// <summary>Bridges the engine to Lunaris on Unity Mono without loading Roslyn facades.</summary>
internal sealed class LunarisHost : IReplHost
{
    private readonly ILog _logger;
    private static readonly string[] _unityHelperSignatures = HelperSignatures.For(
        typeof(UnityHelpers)
    );
    private static readonly IReadOnlyList<Assembly> _additionalAssemblies = new[]
    {
        typeof(UnityHelpers).Assembly,
    };
    private static readonly IReadOnlyList<string> _additionalUsings = MonoCSharpEvaluator
        .DefaultUsings.Concat(new[] { "HotRepl.Helpers.Unity" })
        .ToArray();
    private static readonly IReadOnlyList<EvaluatorCapabilities> _availableEvaluators = new[]
    {
        MonoCSharpEvaluator.MonoCapabilities,
    };

    public LunarisHost(ILog logger, ReplConfig config)
    {
        _logger = logger;
        Config = config;
    }

    public ReplConfig Config { get; }
    public HostInfo HostInfo { get; } =
        new()
        {
            Name = "Lunaris",
            Version = typeof(LunarisPlugin).Assembly.GetName().Version?.ToString() ?? "unknown",
            Runtime = ".NET Framework/Mono",
            Platform = "Unity Mono",
        };
    public IControlCommandRegistry ControlCommands => GlobalControlCommandRegistry.Instance;
    public IReadOnlyList<EvaluatorCapabilities> AvailableEvaluators => _availableEvaluators;
    public string DefaultEvaluatorName => MonoCSharpEvaluator.MonoCapabilities.Name;

    public ICodeEvaluator CreateEvaluator(string evaluatorName)
    {
        if (string.Equals(evaluatorName, DefaultEvaluatorName, StringComparison.Ordinal))
            return new MonoCSharpEvaluator(this);
        throw new NotSupportedException(
            $"Evaluator '{evaluatorName}' is not available in this host."
        );
    }

    public void LogInfo(string message) => _logger.LogInfo(message);

    public void LogDebug(string message) => _logger.LogDebug(message);

    public void LogWarning(string message) => _logger.LogWarning(message);

    public void LogError(string message, Exception? ex = null) =>
        _logger.LogError(ex == null ? message : $"{message}\n{ex}");

    public IReadOnlyList<Assembly> AdditionalAssemblies => _additionalAssemblies;
    public IReadOnlyList<string> AdditionalUsings => _additionalUsings;
    public string[] AdditionalHelperSignatures => _unityHelperSignatures;
}
