namespace HotRepl.Control;

/// <summary>
/// Diagnostic carried in a typed control-command result. Failure
/// diagnostics drive the wire <c>status = "failed"</c> shape;
/// informational diagnostics ride alongside a successful result.
/// </summary>
public sealed record ControlCommandDiagnostic(
    ControlCommandDiagnosticKind Kind,
    string Code,
    string Message,
    bool Retryable = false,
    object? Details = null
);
