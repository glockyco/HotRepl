using System;
using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk;

/// <summary>Base exception for HotRepl SDK failures.</summary>
public class HotReplException : Exception
{
    /// <summary>Create an SDK exception.</summary>
    public HotReplException(
        HotReplErrorKind kind,
        string code,
        string message,
        bool retryable,
        JToken? details = null,
        Exception? innerException = null
    )
        : base(message, innerException)
    {
        Kind = kind;
        Code = code;
        Retryable = retryable;
        Details = details;
    }

    /// <summary>Stable error category.</summary>
    public HotReplErrorKind Kind { get; }

    /// <summary>Stable machine-readable error code.</summary>
    public string Code { get; }

    /// <summary>True when retrying the same operation may succeed.</summary>
    public bool Retryable { get; }

    /// <summary>Optional structured error details.</summary>
    public JToken? Details { get; }
}
