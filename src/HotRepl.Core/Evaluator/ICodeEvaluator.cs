using System.Reflection;

namespace HotRepl.Evaluator;

/// <summary>
/// Compiles and executes C# code using Mono's embedded compiler.
/// All methods MUST be called from the main thread (the thread that calls Tick()).
/// </summary>
internal interface ICodeEvaluator : System.IDisposable
{
    bool IsInitialized { get; }

    /// <summary>
    /// Initializes the compiler context, references all loaded assemblies,
    /// opens default usings, and injects helpers. Idempotent after first success.
    /// Reset() bypasses the idempotency guard and reinitializes unconditionally.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Compiles and executes <paramref name="code"/>.
    /// Returns an EvalOutcome for compile errors, runtime exceptions, and void/value results.
    /// MAY throw <see cref="System.Threading.ThreadAbortException"/> when the watchdog
    /// or a cancel request aborts the thread — the engine is responsible for catching it,
    /// calling Thread.ResetAbort(), and constructing the appropriate Timeout/Cancelled outcome.
    /// All other exceptions are folded into the returned outcome.
    /// </summary>
    EvalOutcome Evaluate(string code);

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
