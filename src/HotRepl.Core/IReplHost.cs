using System.Collections.Generic;
using System.Reflection;
using HotRepl.Control;
using HotRepl.Evaluator;

namespace HotRepl;

/// <summary>
/// Environment provided by the host (BepInEx, MelonLoader, test harness, etc.).
/// Core never imports host-specific types; all coupling flows through this interface.
/// All Log* methods MUST be thread-safe — they may be called from Fleck threads,
/// the main thread, or watchdog timer threads.
/// </summary>
public interface IReplHost
{
    /// <summary>Engine configuration. Read-only after the engine is constructed.</summary>
    ReplConfig Config { get; }

    /// <summary>Metadata about the embedding host, reported in the protocol handshake.</summary>
    HostInfo HostInfo { get; }

    /// <summary>Registry of host-provided typed control-plane commands.</summary>
    IControlCommandRegistry ControlCommands { get; }

    /// <summary>Evaluators this host can construct in the current runtime.</summary>
    IReadOnlyList<EvaluatorCapabilities> AvailableEvaluators { get; }

    /// <summary>Name of the evaluator to use when the engine starts.</summary>
    string DefaultEvaluatorName { get; }

    /// <summary>Create a fresh evaluator instance by capability name.</summary>
    ICodeEvaluator CreateEvaluator(string evaluatorName);

    void LogInfo(string message);
    void LogDebug(string message);
    void LogWarning(string message);
    void LogError(string message, System.Exception? ex = null);

    /// <summary>
    /// Assemblies the host wants additionally referenced in the evaluator.
    /// Used to inject platform-specific helper types without coupling Core to Unity.
    /// </summary>
    IReadOnlyList<Assembly> AdditionalAssemblies { get; }

    /// <summary>
    /// Namespaces opened in addition to evaluator defaults.
    /// E.g. a helper namespace to expose UnityHelpers in eval sessions.
    /// </summary>
    IReadOnlyList<string> AdditionalUsings { get; }

    /// <summary>
    /// Human-readable signatures of any helpers injected via AdditionalAssemblies.
    /// Merged into the handshake helpers[] field so clients know what's available.
    /// </summary>
    string[] AdditionalHelperSignatures { get; }
}
