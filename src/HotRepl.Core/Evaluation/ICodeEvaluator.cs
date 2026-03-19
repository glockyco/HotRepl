using System;

namespace HotRepl.Evaluation
{
    /// <summary>
    /// Compiles and executes C# code at runtime.
    /// Implementations wrap a specific compiler backend (e.g. Mono.CSharp).
    /// </summary>
    /// <remarks>
    /// Internal — only <see cref="HotRepl.Hosting.ReplEngine"/> creates and
    /// consumes evaluators. Not part of the public API surface.
    /// </remarks>
    internal interface ICodeEvaluator : IDisposable
    {
        /// <summary>
        /// Compiles and executes <paramref name="code"/>, returning the result.
        /// This method is called on the host's main thread; implementations
        /// must not spawn additional threads.
        /// </summary>
        EvalResult Evaluate(string code);

        /// <summary>
        /// Returns autocomplete suggestions for the partial code at the given
        /// cursor position. Does not execute any code.
        /// </summary>
        string[] GetCompletions(string code);

    }
}
