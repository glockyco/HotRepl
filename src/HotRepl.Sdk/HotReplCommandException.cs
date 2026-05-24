using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk;

/// <summary>Raised when a command returns a failed result.</summary>
public sealed class HotReplCommandException : HotReplException
{
    /// <summary>Create a command failure.</summary>
    public HotReplCommandException(
        HotReplErrorKind kind,
        string code,
        string message,
        bool retryable,
        JToken? details = null
    )
        : base(kind, code, message, retryable, details) { }
}
