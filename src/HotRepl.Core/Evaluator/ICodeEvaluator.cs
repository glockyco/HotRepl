using System.Reflection;
using System.Threading;

namespace HotRepl.Evaluator;

/// <summary>
/// Compiles and executes C# code at runtime.
/// All methods MUST be called from the main thread that drives <see cref="ReplEngine.Tick"/>.
/// </summary>
public interface ICodeEvaluator : System.IDisposable
{
    bool IsInitialized { get; }

    /// <summary>Capabilities advertised by this evaluator to the engine and clients.</summary>
    EvaluatorCapabilities Capabilities { get; }

    /// <summary>
    /// True when a ScriptEngine hot-reload assembly was detected since the last
    /// Reset(). The engine should perform a full reset and clear this flag.
    /// </summary>
    bool PendingHotReload { get; }

    /// <summary>
    /// Base name of the assembly that triggered the pending hot reload, or null
    /// if no reload is pending.
    /// </summary>
    string? PendingHotReloadAssembly { get; }

    /// <summary>
    /// Initializes the compiler context, references all loaded assemblies,
    /// opens default usings, and injects helpers. Idempotent after first success.
    /// Reset() bypasses the idempotency guard and reinitializes unconditionally.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Compiles and executes <paramref name="code"/>.
    /// Returns an EvalOutcome for compile errors, runtime exceptions, and void/value results.
    /// Cooperative evaluators observe <paramref name="cancellationToken"/> where possible.
    /// All other exceptions are folded into the returned outcome.
    /// </summary>
    EvalOutcome Evaluate(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Returns autocomplete candidates. Never throws; returns empty on any failure.
    /// Does not modify evaluator session state.
    /// </summary>
    CompletionResult Complete(string code, int cursorPos);

    /// <summary>
    /// Tears down the current compiler context and reinitializes unconditionally.
    /// Equivalent to calling Initialize() after clearing all prior state.
    /// </summary>
    void Reset();

    /// <summary>
    /// References an additional assembly in the current compiler session.
    /// Used by HelperInjector and the host to expose platform-specific types.
    /// </summary>
    void ReferenceAssembly(Assembly assembly);

    /// <summary>
    /// Executes a statement directly in the compiler session without queuing,
    /// timeout handling, stdout capture, or history recording.
    /// Used exclusively during initialization (using directives, helper class injection).
    /// </summary>
    void RunInternal(string code);
}
