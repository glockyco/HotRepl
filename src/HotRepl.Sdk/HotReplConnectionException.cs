using System;

namespace HotRepl.Sdk;

/// <summary>Raised when the WebSocket transport cannot connect or fails while active.</summary>
public sealed class HotReplConnectionException : HotReplException
{
    /// <summary>Create a connection failure.</summary>
    public HotReplConnectionException(string message, Exception? innerException = null)
        : base(
            HotReplErrorKind.Connection,
            "connectionFailed",
            message,
            retryable: true,
            innerException: innerException
        ) { }
}
