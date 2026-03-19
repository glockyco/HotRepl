using System;
using System.Reflection;

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
        /// Makes the types in <paramref name="assembly"/> available to
        /// subsequent evaluations.
        /// </summary>
        void AddReference(Assembly assembly);

        /// <summary>
        /// Adds a <c>using</c> directive so future evaluations can reference
        /// types in <paramref name="ns"/> without qualification.
        /// </summary>
        void AddUsing(string ns);

        /// <summary>
        /// Tears down the current evaluator state and reinitializes it,
        /// preserving only the original reference assemblies and usings.
        /// </summary>
        void Reset();
    }
}
