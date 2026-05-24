namespace HotRepl.Sdk;

/// <summary>Raised when the runtime returns an invalid or unexpected protocol frame.</summary>
public sealed class HotReplProtocolException : HotReplException
{
    /// <summary>Create a protocol failure.</summary>
    public HotReplProtocolException(string code, string message)
        : base(HotReplErrorKind.Protocol, code, message, retryable: false) { }
}
