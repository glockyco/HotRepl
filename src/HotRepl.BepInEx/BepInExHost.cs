using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HotRepl;
using HotRepl.BepInEx.Helpers;
using HotRepl.Helpers;

namespace HotRepl.BepInEx;

/// <summary>
/// Bridges the Core ReplEngine to BepInEx 5.x.
/// Provides logging via ManualLogSource and injects UnityHelpers into eval sessions.
/// All Log* methods route to BepInEx's log pipeline, which is thread-safe.
/// </summary>
internal sealed class BepInExHost : IReplHost
{
    private readonly ManualLogSource _logger;

    // Computed once at class load — reflects over the compiled UnityHelpers type.
    private static readonly string[] _unityHelperSignatures =
        HelperInjector.BuildSignatures(typeof(UnityHelpers));

    private static readonly IReadOnlyList<Assembly> _additionalAssemblies =
        new[] { typeof(UnityHelpers).Assembly };

    private static readonly IReadOnlyList<string> _additionalUsings =
        new[] { "HotRepl.BepInEx.Helpers" };

    public BepInExHost(ManualLogSource logger, ReplConfig? config = null)
    {
        _logger = logger;
        Config = config ?? new ReplConfig();
    }

    // ── IReplHost ─────────────────────────────────────────────────────────────

    public ReplConfig Config { get; }

    public void LogInfo(string message) => _logger.LogInfo(message);
    public void LogDebug(string message) => _logger.LogDebug(message);
    public void LogWarning(string message) => _logger.LogWarning(message);
    public void LogError(string message, Exception? ex = null)
    {
        if (ex != null)
            _logger.LogError($"{message}\n{ex}");
        else
            _logger.LogError(message);
    }

    public IReadOnlyList<Assembly> AdditionalAssemblies => _additionalAssemblies;
    public IReadOnlyList<string> AdditionalUsings => _additionalUsings;
    public string[] AdditionalHelperSignatures => _unityHelperSignatures;
}
