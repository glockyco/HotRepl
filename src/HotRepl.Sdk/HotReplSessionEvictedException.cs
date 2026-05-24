namespace HotRepl.Sdk;

/// <summary>Raised when another client replaces this HotRepl session.</summary>
public sealed class HotReplSessionEvictedException : HotReplException
{
    /// <summary>Create a session-evicted failure.</summary>
    public HotReplSessionEvictedException(string reason)
        : base(HotReplErrorKind.SessionEvicted, "sessionEvicted", reason, retryable: false) { }
}
