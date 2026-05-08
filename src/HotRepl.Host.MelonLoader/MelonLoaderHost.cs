using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HotRepl.Control;
using HotRepl.Evaluator;
using HotRepl.Evaluator.Roslyn;
using HotRepl.Helpers;
using HotRepl.Helpers.Il2Cpp;
using HotRepl.Helpers.Unity;
using MelonLoader;

namespace HotRepl.Host.MelonLoader;

internal sealed class MelonLoaderHost : IReplHost
{
    private readonly MelonLogger.Instance _logger;

    private static readonly string[] _helperSignatures = HelperInjector
        .BuildSignatures(typeof(UnityHelpers))
        .Concat(HelperInjector.BuildSignatures(typeof(Il2CppHelpers)))
        .ToArray();

    private static readonly IReadOnlyList<Assembly> _additionalAssemblies = new[]
    {
        typeof(UnityHelpers).Assembly,
        typeof(Il2CppHelpers).Assembly,
    };

    private static readonly IReadOnlyList<string> _additionalUsings = new[]
    {
        "HotRepl.Helpers.Unity",
        "HotRepl.Helpers.Il2Cpp",
        "Il2CppInterop.Runtime",
    };

    public MelonLoaderHost(MelonLogger.Instance logger, ReplConfig config = null)
    {
        _logger = logger;
        Config = config ?? new ReplConfig();
    }

    public ReplConfig Config { get; }

    public HostInfo HostInfo { get; } =
        new()
        {
            Name = "MelonLoader",
            Version = "0.x",
            Runtime = ".NET 6",
            Platform = "Unity IL2CPP",
        };

    public IControlCommandRegistry ControlCommands => GlobalControlCommandRegistry.Instance;

    public IReadOnlyList<EvaluatorCapabilities> AvailableEvaluators =>
        RoslynEvaluatorFactory.Capabilities;

    public string DefaultEvaluatorName =>
        Config.DefaultEvaluatorName ?? RoslynScriptEvaluator.ScriptCapabilities.Name;

    public ICodeEvaluator CreateEvaluator(string evaluatorName) =>
        RoslynEvaluatorFactory.Create(evaluatorName, this);

    public IReadOnlyList<Assembly> AdditionalAssemblies => _additionalAssemblies;
    public IReadOnlyList<string> AdditionalUsings => _additionalUsings;
    public string[] AdditionalHelperSignatures => _helperSignatures;

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
