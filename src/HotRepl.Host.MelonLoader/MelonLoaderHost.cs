using System;
using System.Collections.Generic;
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

    public MelonLoaderHost(MelonLogger.Instance logger, ReplConfig config = null)
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

    public void LogError(string message, Exception ex = null)
    {
        if (ex != null)
            _logger.Error($"{message}\n{ex}");
        else
            _logger.Error(message);
    }
}
