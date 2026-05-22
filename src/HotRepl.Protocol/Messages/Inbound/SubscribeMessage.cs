using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Request to subscribe to repeated expression evaluation.</summary>
public sealed record SubscribeMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.Subscribe;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("code")]
    public string Code { get; set; } = string.Empty;

    [JsonProperty("intervalFrames")]
    public int? IntervalFrames { get; set; }

    [JsonProperty("onChange")]
    public bool? OnChange { get; set; }

    [JsonProperty("limit")]
    public int? Limit { get; set; }

    [JsonProperty("timeoutMs")]
    public int? TimeoutMs { get; set; }
}
