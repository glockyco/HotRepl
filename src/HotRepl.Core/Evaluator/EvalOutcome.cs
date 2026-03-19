namespace HotRepl.Evaluator;

/// <summary>
/// The result of one evaluation. Always returned, never thrown.
/// ThreadAbortException is caught inside Evaluate() and converted to Aborted().
/// </summary>
internal sealed class EvalOutcome
{
    public bool Success { get; private init; }
    public bool HasValue { get; private init; }
    public object? Value { get; private init; }
    public string? ValueType { get; private init; }
    public string? Stdout { get; private init; }
    public long DurationMs { get; private init; }
    public string? ErrorKind { get; private init; }
    public string? ErrorMessage { get; private init; }
    public string? StackTrace { get; private init; }

    private EvalOutcome() { }

    public static EvalOutcome Ok(object? value, string? valueType, string? stdout, long durationMs) =>
        new() { Success = true, HasValue = value != null, Value = value, ValueType = valueType, Stdout = stdout, DurationMs = durationMs };

    public static EvalOutcome OkVoid(string? stdout, long durationMs) =>
        new() { Success = true, HasValue = false, Stdout = stdout, DurationMs = durationMs };

    public static EvalOutcome CompileError(string message, string? stdout, long durationMs) =>
        new() { Success = false, ErrorKind = Protocol.ErrorKind.Compile, ErrorMessage = message, Stdout = stdout, DurationMs = durationMs };

    public static EvalOutcome RuntimeError(string message, string? stackTrace, string? stdout, long durationMs) =>
        new() { Success = false, ErrorKind = Protocol.ErrorKind.Runtime, ErrorMessage = message, StackTrace = stackTrace, Stdout = stdout, DurationMs = durationMs };

    /// <summary>Produced by the engine when the watchdog fires. No stdout — TAE interrupted the capture.</summary>
    public static EvalOutcome Timeout(long durationMs) =>
        new() { Success = false, ErrorKind = Protocol.ErrorKind.Timeout, ErrorMessage = "Evaluation timed out.", DurationMs = durationMs };

    /// <summary>Produced by the engine when a cancel request matched. No stdout — TAE interrupted the capture.</summary>
    public static EvalOutcome Cancelled(long durationMs) =>
        new() { Success = false, ErrorKind = Protocol.ErrorKind.Cancelled, ErrorMessage = "Evaluation cancelled.", DurationMs = durationMs };

    /// <summary>
    /// Sentinel returned when ThreadAbortException was caught in Evaluate().
    /// RunGuarded inspects _timedOut to determine whether this was a watchdog timeout
    /// or a cancel request, then replaces this with Timeout() or Cancelled().
    /// Never sent to the client directly.
    /// </summary>
    internal static readonly EvalOutcome Aborted = new() { Success = false, ErrorKind = "aborted" };
}
