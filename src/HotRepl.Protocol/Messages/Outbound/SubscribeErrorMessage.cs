using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Subscription error response.</summary>
public sealed record SubscribeErrorMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.SubscribeError;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("seq")]
    public long Seq { get; set; }

    [JsonProperty("error")]
    public HotReplErrorEnvelope Error { get; set; } =
        new(ErrorKind.Internal, "internal", "Internal error.", retryable: false, details: null);

    [JsonProperty("final")]
    public bool Final { get; set; }
}
