using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Protocol;

/// <summary>Subscription value response.</summary>
public sealed class SubscribeResultMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.SubscribeResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("seq")]
    public long Seq { get; set; }

    [JsonProperty("value")]
    public JToken? Value { get; set; }

    [JsonProperty("final")]
    public bool Final { get; set; }
}
