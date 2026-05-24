namespace HotRepl.Control;

/// <summary>Stable diagnostic taxonomy for <see cref="ControlCommandDiagnostic"/>.</summary>
public enum ControlCommandDiagnosticKind
{
    /// <summary>Informational note attached to a successful result.</summary>
    Info,

    /// <summary>Non-fatal warning attached to a successful result.</summary>
    Warning,

    /// <summary>Inbound arguments failed schema validation; handler did not run.</summary>
    ValidationFailed,

    /// <summary>A required runtime precondition was not satisfied.</summary>
    PreconditionFailed,

    /// <summary>A conflicting operation prevented the command from succeeding.</summary>
    Conflict,

    /// <summary>The operation was cancelled (by caller or runtime).</summary>
    Cancelled,
}
