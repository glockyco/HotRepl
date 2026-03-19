namespace HotRepl.Evaluation
{
    /// <summary>
    /// Immutable outcome of a single code evaluation, sent back to the client.
    /// </summary>
    public sealed class EvalResult
    {
        /// <summary>Whether the code compiled and executed without error.</summary>
        public bool Success { get; init; }

        /// <summary>Whether the expression produced a return value.</summary>
        public bool HasValue { get; init; }

        /// <summary>The return value, if any. Null when <see cref="HasValue"/> is false.</summary>
        public object? Value { get; init; }

        /// <summary>
        /// Assembly-qualified type name of <see cref="Value"/>, or null if no value.
        /// Useful for client-side formatting when the raw object isn't serializable.
        /// </summary>
        public string? ValueType { get; init; }

        /// <summary>Error message on failure. Null on success.</summary>
        public string? Error { get; init; }

        /// <summary>Stack trace when a runtime exception occurred. Null otherwise.</summary>
        public string? StackTrace { get; init; }

        /// <summary>
        /// Discriminator for the error source: <c>"compilation"</c> or <c>"runtime"</c>.
        /// Null on success.
        /// </summary>
        public string? ErrorKind { get; init; }

        /// <summary>
        /// Captured <see cref="System.Console.Out"/> output produced during evaluation.
        /// </summary>
        public string? Stdout { get; init; }

        /// <summary>Wall-clock milliseconds the evaluation took.</summary>
        public long DurationMs { get; init; }

        /// <summary>Creates a successful result with a value.</summary>
        internal static EvalResult Ok(object? value, string? valueType, string? stdout, long durationMs) =>
            new()
            {
                Success = true,
                HasValue = value != null,
                Value = value,
                ValueType = valueType,
                Stdout = stdout,
                DurationMs = durationMs
            };

        /// <summary>Creates a successful result with no return value.</summary>
        internal static EvalResult OkVoid(string? stdout, long durationMs) =>
            new()
            {
                Success = true,
                HasValue = false,
                Stdout = stdout,
                DurationMs = durationMs
            };

        /// <summary>Creates a compilation-error result.</summary>
        internal static EvalResult CompilationError(string error, string? stdout, long durationMs) =>
            new()
            {
                Success = false,
                Error = error,
                ErrorKind = "compilation",
                Stdout = stdout,
                DurationMs = durationMs
            };

        /// <summary>Creates a runtime-error result.</summary>
        internal static EvalResult RuntimeError(string error, string? stackTrace, string? stdout, long durationMs) =>
            new()
            {
                Success = false,
                Error = error,
                ErrorKind = "runtime",
                StackTrace = stackTrace,
                Stdout = stdout,
                DurationMs = durationMs
            };

        /// <summary>Creates a timeout/cancelled result.</summary>
        internal static EvalResult Timeout(bool cancelled, long durationMs) =>
            new()
            {
                Success = false,
                Error = cancelled ? "Evaluation cancelled." : "Evaluation timed out.",
                ErrorKind = cancelled ? "cancelled" : "timeout",
                DurationMs = durationMs
            };
    }
}
