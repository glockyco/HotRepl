using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class SubscribeMessage
{
    [JsonProperty("type")]
    public string Type { get; init; } = MessageType.Subscribe;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("code")]
    public string Code { get; set; } = string.Empty;

    [JsonProperty("intervalFrames")]
    public int IntervalFrames { get; set; } = 1;

    [JsonProperty("onChange")]
    public bool OnChange { get; set; }

    [JsonProperty("limit")]
    public int Limit { get; set; }

    [JsonProperty("timeoutMs")]
    public int TimeoutMs { get; set; }
}
