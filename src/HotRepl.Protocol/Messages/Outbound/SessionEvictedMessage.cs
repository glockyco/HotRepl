using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Notification sent to a displaced connection before the socket closes.</summary>
public sealed class SessionEvictedMessage
{
    /// <summary>Wire message type.</summary>
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.SessionEvicted;

    /// <summary>Eviction reason.</summary>
    [JsonProperty("reason")]
    public string Reason { get; set; } = "displaced";

    /// <summary>Information about the replacing client.</summary>
    [JsonProperty("by")]
    public SessionEvictedBy By { get; set; } = new();
}
