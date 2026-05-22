namespace HotRepl.Protocol;

/// <summary>Closed set of machine-readable v2 error kinds.</summary>
public static class ErrorKind
{
    /// <summary>Input or output failed schema validation.</summary>
    public const string ValidationFailed = "validation_failed";

    /// <summary>A required precondition was not satisfied.</summary>
    public const string PreconditionFailed = "precondition_failed";

    /// <summary>The request conflicts with current runtime state.</summary>
    public const string Conflict = "conflict";

    /// <summary>The operation exceeded its time budget.</summary>
    public const string Timeout = "timeout";

    /// <summary>The operation was cancelled.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The runtime is temporarily overloaded or the result is not ready.</summary>
    public const string Busy = "busy";

    /// <summary>The requested command name is not registered.</summary>
    public const string UnknownCommand = "unknown_command";

    /// <summary>The target runtime does not support the requested operation.</summary>
    public const string UnsupportedOperation = "unsupported_operation";

    /// <summary>An artifact reference cannot be resolved.</summary>
    public const string ArtifactMissing = "artifact_missing";

    /// <summary>The wire request is malformed or invalid for the protocol.</summary>
    public const string InvalidRequest = "invalid_request";

    /// <summary>An unexpected runtime failure occurred.</summary>
    public const string Internal = "internal";
}
