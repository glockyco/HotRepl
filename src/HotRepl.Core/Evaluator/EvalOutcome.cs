namespace HotRepl.Evaluator;

/// <summary>
/// The result of one evaluation. Always returned, never thrown.
/// ThreadAbortException is caught inside Evaluate() and converted to Aborted().
/// </summary>
public sealed class EvalOutcome
{
    public bool Success { get; private init; }
    public bool HasValue { get; private init; }
    public object? Value { get; private init; }
    public string? ValueType { get; private init; }

    /// <summary>
    /// Console output that the engine collected during the evaluation, or null when there was none.
    /// Evaluators leave it unset, and the engine attaches it with <see cref="WithStdout"/>.
    /// </summary>
    public string? Stdout { get; private init; }

    public long DurationMs { get; private init; }
    public string? ErrorKind { get; private init; }
    public string? ErrorMessage { get; private init; }
    public string? StackTrace { get; private init; }

    private EvalOutcome() { }

    public static EvalOutcome Ok(object? value, string? valueType, long durationMs) =>
        new()
        {
            Success = true,
            HasValue = value != null,
            Value = value,
            ValueType = valueType,
            DurationMs = durationMs,
        };

    public static EvalOutcome OkVoid(long durationMs) =>
        new()
        {
            Success = true,
            HasValue = false,
            DurationMs = durationMs,
        };

    public static EvalOutcome CompileError(string message, long durationMs) =>
        new()
        {
            Success = false,
            ErrorKind = Protocol.ErrorKind.ValidationFailed,
            ErrorMessage = message,
            DurationMs = durationMs,
        };

    public static EvalOutcome RuntimeError(string message, string? stackTrace, long durationMs) =>
        new()
        {
            Success = false,
            ErrorKind = Protocol.ErrorKind.Internal,
            ErrorMessage = message,
            StackTrace = stackTrace,
            DurationMs = durationMs,
        };

    /// <summary>Produced by the engine when the watchdog fires.</summary>
    public static EvalOutcome Timeout(long durationMs) =>
        new()
        {
            Success = false,
            ErrorKind = Protocol.ErrorKind.Timeout,
            ErrorMessage = "Evaluation timed out.",
            DurationMs = durationMs,
        };

    /// <summary>Produced by the engine when a cancel request matched.</summary>
    public static EvalOutcome Cancelled(long durationMs) =>
        new()
        {
            Success = false,
            ErrorKind = Protocol.ErrorKind.Cancelled,
            ErrorMessage = "Evaluation cancelled.",
            DurationMs = durationMs,
        };

    /// <summary>
    /// Sentinel returned when ThreadAbortException was caught in Evaluate().
    /// RunGuarded inspects _timedOut to determine whether this was a watchdog timeout
    /// or a cancel request, then replaces this with Timeout() or Cancelled().
    /// Never sent to the client directly.
    /// </summary>
    public static readonly EvalOutcome Aborted = new() { Success = false, ErrorKind = "aborted" };

    /// <summary>Returns a copy of this outcome that carries the given console output.</summary>
    public EvalOutcome WithStdout(string? stdout) =>
        new()
        {
            Success = Success,
            HasValue = HasValue,
            Value = Value,
            ValueType = ValueType,
            Stdout = stdout,
            DurationMs = DurationMs,
            ErrorKind = ErrorKind,
            ErrorMessage = ErrorMessage,
            StackTrace = StackTrace,
        };
}
