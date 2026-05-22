using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Addressed protocol-level error response.</summary>
public sealed class ProtocolErrorMessage
{
    /// <summary>Wire message type.</summary>
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.Error;

    /// <summary>Caller-supplied request id when one could be parsed.</summary>
    [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
    public string? Id { get; set; }

    /// <summary>Universal v2 error envelope.</summary>
    [JsonProperty("error")]
    public HotReplErrorEnvelope Error { get; set; } = new(
        ErrorKind.InvalidRequest,
        "invalidRequest",
        "Invalid request.",
        retryable: false,
        details: null
    );
}
