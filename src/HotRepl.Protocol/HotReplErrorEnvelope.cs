using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Protocol;

/// <summary>Universal v2 error envelope returned by protocol and command failures.</summary>
public sealed record HotReplErrorEnvelope
{
    /// <summary>Creates an error envelope.</summary>
    public HotReplErrorEnvelope(
        string kind,
        string code,
        string message,
        bool retryable,
        JToken? details
    )
    {
        Kind = kind;
        Code = code;
        Message = message;
        Retryable = retryable;
        Details = details;
    }

    /// <summary>Stable closed error kind.</summary>
    [JsonProperty("kind")]
    public string Kind { get; }

    /// <summary>Stable per-handler or per-runtime error code.</summary>
    [JsonProperty("code")]
    public string Code { get; }

    /// <summary>Human-readable diagnostic message.</summary>
    [JsonProperty("message")]
    public string Message { get; }

    /// <summary>Whether retrying the same request may succeed.</summary>
    [JsonProperty("retryable")]
    public bool Retryable { get; }

    /// <summary>Optional structured details.</summary>
    [JsonProperty("details", NullValueHandling = NullValueHandling.Ignore)]
    public JToken? Details { get; }
}
