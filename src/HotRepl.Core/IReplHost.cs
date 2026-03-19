using System.Collections.Generic;
using System.Reflection;

namespace HotRepl;

/// <summary>
/// Environment provided by the host (BepInEx, test harness, etc.).
/// Core never imports host-specific types; all coupling flows through this interface.
/// All Log* methods MUST be thread-safe — they may be called from Fleck threads,
/// the main thread, or watchdog timer threads.
/// </summary>
public interface IReplHost
{
    /// <summary>Engine configuration. Read-only after the engine is constructed.</summary>
    ReplConfig Config { get; }

    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message, System.Exception? ex = null);

    /// <summary>
    /// Assemblies the host wants additionally referenced in the evaluator.
    /// Used to inject platform-specific helper types (e.g. the BepInEx assembly
    /// containing UnityHelpers) without coupling Core to Unity.
    /// </summary>
    IReadOnlyList<Assembly> AdditionalAssemblies { get; }

    /// <summary>
    /// Namespaces opened in addition to the Core defaults.
    /// E.g. "HotRepl.BepInEx.Helpers" to expose UnityHelpers in eval sessions.
    /// </summary>
    IReadOnlyList<string> AdditionalUsings { get; }

    /// <summary>
    /// Human-readable signatures of any helpers injected via AdditionalAssemblies.
    /// Merged into the handshake helpers[] field so clients know what's available.
    /// </summary>
    string[] AdditionalHelperSignatures { get; }
}
