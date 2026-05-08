using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class SubscribeResultMessage
{
    [JsonProperty("type")]
    public string Type { get; } = MessageType.SubscribeResult;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("seq")]
    public int Seq { get; set; }

    [JsonProperty("hasValue")]
    public bool HasValue { get; set; }

    [JsonProperty("value")]
    public string? Value { get; set; }

    [JsonProperty("valueType")]
    public string? ValueType { get; set; }

    [JsonProperty("durationMs")]
    public long DurationMs { get; set; }

    [JsonProperty("final")]
    public bool Final { get; set; }
}
