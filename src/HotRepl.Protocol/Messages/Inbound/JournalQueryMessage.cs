using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Request to query server-side journal entries.</summary>
public sealed record JournalQueryMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = MessageType.JournalQuery;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("kind")]
    public string? Kind { get; set; }

    [JsonProperty("limit")]
    public int? Limit { get; set; }
}
