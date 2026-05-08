using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class LeaseAcquireMessage
{
    [JsonProperty("type")]
    public string Type { get; init; } = MessageType.LeaseAcquire;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonProperty("clientName")]
    public string ClientName { get; set; } = string.Empty;
}
