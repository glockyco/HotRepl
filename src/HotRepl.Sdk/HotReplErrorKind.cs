namespace HotRepl.Sdk;

/// <summary>SDK-level classification for HotRepl failures.</summary>
public enum HotReplErrorKind
{
    /// <summary>Unexpected server or client failure.</summary>
    Internal,

    /// <summary>The request was malformed or invalid for the target command.</summary>
    InvalidRequest,

    /// <summary>The runtime does not support the requested operation.</summary>
    UnsupportedOperation,

    /// <summary>Input failed schema or business validation.</summary>
    ValidationFailed,

    /// <summary>A runtime precondition was not met.</summary>
    PreconditionFailed,

    /// <summary>The request conflicts with current runtime state.</summary>
    Conflict,

    /// <summary>The operation was cancelled.</summary>
    Cancelled,

    /// <summary>The session was replaced by another client.</summary>
    SessionEvicted,

    /// <summary>The SDK could not connect or the transport failed.</summary>
    Connection,

    /// <summary>The operation timed out.</summary>
    Timeout,

    /// <summary>The runtime returned an invalid protocol shape.</summary>
    Protocol,
}
