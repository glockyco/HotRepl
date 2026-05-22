using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Failed eval response.</summary>
public sealed record EvalErrorMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.EvalError;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("error")]
    public HotReplErrorEnvelope Error { get; set; } = new(
        ErrorKind.Internal,
        "internal",
        "Internal error.",
        retryable: false,
        details: null
    );
}
